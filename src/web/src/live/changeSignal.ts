/**
 * Live-update client core (ADR-008). The Server names an area that changed and nothing
 * else; this module turns that into "run your refetch". It never reads data from the
 * signal and never replaces polling: polling stays as the fallback and as a slow safety net.
 */

export const CHANGE_AREAS = ['executions', 'recommendations', 'configured-logic', 'assets', 'modules', 'connections'] as const;
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

type Subscription = { areas: ReadonlySet<ChangeArea>; handler: () => void };

export class ChangeSignalClient {
  private current: LiveState = 'idle';
  private connection: HubConnectionLike | null = null;
  private enabled = false;
  private attempt = 0;
  private retryHandle: unknown = null;
  /** Bumped on every enable or disable so a stale start() result is ignored. */
  private generation = 0;
  private readonly subscriptions = new Set<Subscription>();
  private readonly listeners = new Set<() => void>();

  constructor(
    private readonly factory: ConnectionFactory,
    private readonly timers: Timers = browserTimers,
    private readonly retryDelays: readonly number[] = START_RETRY_DELAYS_MS,
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
  subscribe(areas: readonly ChangeArea[], handler: () => void): () => void {
    const subscription: Subscription = { areas: new Set(areas), handler };
    this.subscriptions.add(subscription);
    return () => { this.subscriptions.delete(subscription); };
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
    connection.on(HUB_METHOD, (...args: unknown[]) => {
      if (generation !== this.generation) return;
      const area = args[0];
      if (isChangeArea(area)) this.deliver(area);
    });
    connection.onreconnecting(() => {
      if (generation === this.generation) this.setState('reconnecting');
    });
    connection.onreconnected(() => {
      if (generation !== this.generation) return;
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
  private deliver(area: ChangeArea | null): void {
    for (const subscription of [...this.subscriptions]) {
      if (area !== null && !subscription.areas.has(area)) continue;
      try {
        subscription.handler();
      } catch {
        // A failing refetch must not stop other subscribers; polling recovers.
      }
    }
  }

  private setState(next: LiveState): void {
    if (next === this.current) return;
    this.current = next;
    for (const listener of [...this.listeners]) listener();
  }
}
