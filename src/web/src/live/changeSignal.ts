/**
 * Live-update client core (ADR-008). The Server names an area that changed and nothing
 * else; this module turns that into "run your refetch". It never reads data from the
 * signal and never replaces polling: polling stays as the fallback and as a slow safety net.
 */

export const CHANGE_AREAS = ['executions', 'recommendations', 'configured-logic', 'assets', 'modules', 'connections', 'control'] as const;
export type ChangeArea = (typeof CHANGE_AREAS)[number];

export function isChangeArea(value: unknown): value is ChangeArea {
  return typeof value === 'string' && (CHANGE_AREAS as readonly string[]).includes(value);
}

/**
 * idle: not asked to connect (no session yet, or live updates switched off).
 * connecting: first attempt in progress. connected: signals can arrive.
 * reconnecting: lost the connection, retrying quickly. disconnected: the hub is unavailable
 * (absent, refused or unreachable); retried slowly in the background.
 */
export type LiveState = 'idle' | 'connecting' | 'connected' | 'reconnecting' | 'disconnected';

export const HUB_PATH = '/hubs/changes';
export const HUB_METHOD = 'changed';
export const EVENT_METHOD = 'event';
/**
 * The Server sends the typed event first and the coalesced area signal (up to about 250 ms) after it.
 * A signal this soon after an event of the same area is the same change and is not acted on twice.
 */
export const EVENT_COVERS_SIGNAL_MS = 2_000;

/** One entity change (docs/design/realtime-events.md). The payload holds short state labels only. */
export type ChangeEvent = {
  epoch: string;
  area: ChangeArea;
  /** Counts per area within one epoch; a skipped number means an event was lost. */
  sequence: number;
  entity: string;
  id: string;
  kind: string;
  occurredAt: string;
  actor: string | null;
  correlationId: string | null;
  payload: Readonly<Record<string, string | null>>;
};

export function parseChangeEvent(value: unknown): ChangeEvent | null {
  if (!value || typeof value !== 'object') return null;
  const raw = value as Record<string, unknown>;
  if (!isChangeArea(raw.area) || typeof raw.epoch !== 'string' || typeof raw.entity !== 'string' ||
      typeof raw.id !== 'string' || typeof raw.kind !== 'string' ||
      typeof raw.sequence !== 'number' || !Number.isSafeInteger(raw.sequence) || raw.sequence < 1) return null;
  const payload: Record<string, string | null> = {};
  if (raw.payload && typeof raw.payload === 'object') {
    for (const [key, item] of Object.entries(raw.payload as Record<string, unknown>)) {
      if (typeof item === 'string' || item === null) payload[key] = item;
    }
  }
  return {
    epoch: raw.epoch, area: raw.area, sequence: raw.sequence, entity: raw.entity, id: raw.id, kind: raw.kind,
    occurredAt: typeof raw.occurredAt === 'string' ? raw.occurredAt : '',
    actor: typeof raw.actor === 'string' ? raw.actor : null,
    correlationId: typeof raw.correlationId === 'string' ? raw.correlationId : null,
    payload,
  };
}
export const POLL_INTERVAL_FALLBACK_MS = 10_000;
export const POLL_INTERVAL_LIVE_MS = 60_000;
/** Slow retry schedule for a hub that could not be started (milliseconds). The last value repeats. */
export const START_RETRY_DELAYS_MS: readonly number[] = [5_000, 15_000, 30_000, 60_000, 120_000, 300_000];

/** Connected means signals arrive, so polling only needs to be a safety net. */
export function pollIntervalMs(state: LiveState): number {
  return state === 'connected' ? POLL_INTERVAL_LIVE_MS : POLL_INTERVAL_FALLBACK_MS;
}

/** The part of a SignalR HubConnection this client uses; tests supply a fake. */
export interface HubConnectionLike {
  start(): Promise<void>;
  stop(): Promise<void>;
  on(method: string, handler: (...args: unknown[]) => void): void;
  onclose(handler: (error?: Error) => void): void;
  onreconnecting(handler: (error?: Error) => void): void;
  onreconnected(handler: (connectionId?: string) => void): void;
}

export type ConnectionFactory = () => HubConnectionLike;

export type Timers = {
  set(callback: () => void, delayMs: number): unknown;
  clear(handle: unknown): void;
};

const browserTimers: Timers = {
  set: (callback, delayMs) => globalThis.setTimeout(callback, delayMs),
  clear: handle => globalThis.clearTimeout(handle as ReturnType<typeof setTimeout>),
};

export type SubscribeOptions = {
  /** Skip an area signal that an event of the same area just announced; the handler already applied it. */
  skipWhenCovered?: boolean;
};

type Subscription = { areas: ReadonlySet<ChangeArea>; handler: () => void; skipWhenCovered: boolean };

export class ChangeSignalClient {
  private current: LiveState = 'idle';
  private connection: HubConnectionLike | null = null;
  private enabled = false;
  private attempt = 0;
  private retryHandle: unknown = null;
  /** Bumped on every enable or disable so a stale start() result is ignored. */
  private generation = 0;
  private readonly subscriptions = new Set<Subscription>();
  private readonly eventSubscriptions = new Set<(event: ChangeEvent) => void>();
  private readonly listeners = new Set<() => void>();
  /** Last event seen per area; forgotten on every (re)connect because the catch-up read replaces it. */
  private readonly streams = new Map<ChangeArea, { epoch: string; sequence: number }>();
  private readonly lastEventAt = new Map<ChangeArea, number>();

  constructor(
    private readonly factory: ConnectionFactory,
    private readonly timers: Timers = browserTimers,
    private readonly retryDelays: readonly number[] = START_RETRY_DELAYS_MS,
    private readonly now: () => number = () => Date.now(),
  ) {}

  get state(): LiveState {
    return this.current;
  }

  /** Subscribe to state changes; the function form suits React's useSyncExternalStore. */
  onStateChange = (listener: () => void): (() => void) => {
    this.listeners.add(listener);
    return () => { this.listeners.delete(listener); };
  };

  /**
   * Run `handler` when any of `areas` changes, and once after every (re)connect, because
   * signals sent while disconnected are lost. The handler should refetch; it receives no data.
   */
  subscribe(areas: readonly ChangeArea[], handler: () => void, options: SubscribeOptions = {}): () => void {
    const subscription: Subscription = {
      areas: new Set(areas), handler, skipWhenCovered: options.skipWhenCovered === true,
    };
    this.subscriptions.add(subscription);
    return () => { this.subscriptions.delete(subscription); };
  }

  /**
   * Receive typed events in order. A gap, a server restart (new epoch) or a lost event is never
   * passed on as an event: it becomes an area refetch for the area subscribers instead.
   */
  subscribeEvents(handler: (event: ChangeEvent) => void): () => void {
    this.eventSubscriptions.add(handler);
    return () => { this.eventSubscriptions.delete(handler); };
  }

  setEnabled(enabled: boolean): void {
    if (enabled === this.enabled) return;
    this.enabled = enabled;
    this.generation += 1;
    if (enabled) {
      this.attempt = 0;
      void this.open();
    } else {
      this.clearRetry();
      const connection = this.connection;
      this.connection = null;
      this.setState('idle');
      if (connection) void connection.stop().catch(() => undefined);
    }
  }

  private async open(): Promise<void> {
    const generation = this.generation;
    this.clearRetry();
    // A background retry keeps showing the fallback state instead of flickering.
    if (this.current === 'idle') this.setState('connecting');
    let connection: HubConnectionLike;
    try {
      connection = this.factory();
    } catch {
      this.failedToStart(generation);
      return;
    }
    this.connection = connection;
    this.streams.clear();
    // Registered before 'changed' on purpose; neither depends on the order.
    connection.on(EVENT_METHOD, (...args: unknown[]) => {
      if (generation !== this.generation) return;
      const event = parseChangeEvent(args[0]);
      if (event) this.receive(event);
    });
    connection.on(HUB_METHOD, (...args: unknown[]) => {
      if (generation !== this.generation) return;
      const area = args[0];
      if (isChangeArea(area)) this.deliver(area, true);
    });
    connection.onreconnecting(() => {
      if (generation === this.generation) this.setState('reconnecting');
    });
    connection.onreconnected(() => {
      if (generation !== this.generation) return;
      this.streams.clear();
      this.setState('connected');
      this.deliver(null);
    });
    connection.onclose(() => {
      if (generation !== this.generation) return;
      this.connection = null;
      this.failedToStart(generation);
    });
    try {
      await connection.start();
    } catch {
      if (generation !== this.generation) return;
      // start() failed, so onclose does not fire; the connection object is unusable.
      this.connection = null;
      void connection.stop().catch(() => undefined);
      this.failedToStart(generation);
      return;
    }
    if (generation !== this.generation) {
      void connection.stop().catch(() => undefined);
      return;
    }
    this.attempt = 0;
    this.setState('connected');
    this.deliver(null);
  }

  private failedToStart(generation: number): void {
    if (generation !== this.generation || !this.enabled) return;
    this.clearRetry();
    this.setState('disconnected');
    const delay = this.retryDelays[Math.min(this.attempt, this.retryDelays.length - 1)] ?? 300_000;
    this.attempt += 1;
    this.retryHandle = this.timers.set(() => {
      this.retryHandle = null;
      if (generation === this.generation && this.enabled) void this.open();
    }, delay);
  }

  private clearRetry(): void {
    if (this.retryHandle !== null) this.timers.clear(this.retryHandle);
    this.retryHandle = null;
  }

  /** `area` null means "everything may have changed" (after connect). Each handler runs at most once. */
  private deliver(area: ChangeArea | null, fromSignal = false): void {
    const covered = fromSignal && area !== null && this.isCovered(area);
    for (const subscription of [...this.subscriptions]) {
      if (area !== null && !subscription.areas.has(area)) continue;
      if (covered && subscription.skipWhenCovered) continue;
      try {
        subscription.handler();
      } catch {
        // A failing refetch must not stop other subscribers; polling recovers.
      }
    }
  }

  private isCovered(area: ChangeArea): boolean {
    const at = this.lastEventAt.get(area);
    return at !== undefined && this.now() - at <= EVENT_COVERS_SIGNAL_MS;
  }

  private receive(event: ChangeEvent): void {
    const previous = this.streams.get(event.area);
    // Same epoch and an old number: a repeat, already applied.
    if (previous && previous.epoch === event.epoch && event.sequence <= previous.sequence) return;
    this.streams.set(event.area, { epoch: event.epoch, sequence: event.sequence });
    this.lastEventAt.set(event.area, this.now());
    // The first event after a connect is the baseline: the catch-up read already covers what came before it.
    if (previous && (previous.epoch !== event.epoch || event.sequence !== previous.sequence + 1)) {
      this.deliver(event.area);
      return;
    }
    for (const handler of [...this.eventSubscriptions]) {
      try {
        handler(event);
      } catch {
        // One failing view must not stop the others; the area signal and polling recover it.
      }
    }
  }

  private setState(next: LiveState): void {
    if (next === this.current) return;
    this.current = next;
    for (const listener of [...this.listeners]) listener();
  }
}
