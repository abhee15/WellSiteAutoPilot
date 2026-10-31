import { createContext, useContext, useEffect, useMemo, useRef, useState, useSyncExternalStore } from 'react';
import type { ReactNode } from 'react';
import { ChangeSignalClient, POLL_INTERVAL_FALLBACK_MS, isChangeArea, pollIntervalMs } from './changeSignal';
import type { ChangeArea, ChangeEvent, LiveState, SubscribeOptions } from './changeSignal';
import { createSignalrConnection } from './signalrConnection';

export type LiveUpdates = {
  state: LiveState;
  /** How often a view should still poll, given the connection state. */
  pollIntervalMs: number;
  subscribe(areas: readonly ChangeArea[], handler: () => void, options?: SubscribeOptions): () => void;
  /** Typed entity events, in order and without gaps; a gap becomes an area refetch instead. */
  subscribeEvents(handler: (event: ChangeEvent) => void): () => void;
  /** Connect only while there is a signed-in session; disconnect otherwise. */
  setEnabled(enabled: boolean): void;
};

/**
 * Without a provider (tests, previews) live updates are off and views poll every 10 seconds,
 * exactly as before.
 */
const inert: LiveUpdates = {
  state: 'idle',
  pollIntervalMs: POLL_INTERVAL_FALLBACK_MS,
  subscribe: () => () => undefined,
  subscribeEvents: () => () => undefined,
  setEnabled: () => undefined,
};

const LiveUpdatesContext = createContext<LiveUpdates>(inert);

export function LiveUpdatesProvider({ client: provided, children }: { client?: ChangeSignalClient; children: ReactNode }) {
  const [client] = useState(() => provided ?? new ChangeSignalClient(createSignalrConnection));
  const state = useSyncExternalStore(client.onStateChange, () => client.state, () => 'idle' as LiveState);
  // subscribe and setEnabled keep their identity for the life of the client, so effects that
  // depend on them do not re-run on every connection state change.
  const actions = useMemo(() => ({
    subscribe: (areas: readonly ChangeArea[], handler: () => void, options?: SubscribeOptions) =>
      client.subscribe(areas, handler, options),
    subscribeEvents: (handler: (event: ChangeEvent) => void) => client.subscribeEvents(handler),
    setEnabled: (enabled: boolean) => client.setEnabled(enabled),
  }), [client]);
  const value = useMemo<LiveUpdates>(
    () => ({ state, pollIntervalMs: pollIntervalMs(state), ...actions }), [state, actions]);
  useEffect(() => () => client.setEnabled(false), [client]);
  return <LiveUpdatesContext.Provider value={value}>{children}</LiveUpdatesContext.Provider>;
}

export function useLiveUpdates(): LiveUpdates {
  return useContext(LiveUpdatesContext);
}

/** Run `refresh` when the Server reports a change in any of `areas`. */
export function useChangeRefresh(areas: readonly ChangeArea[], refresh: () => void, options?: SubscribeOptions): void {
  const { subscribe } = useLiveUpdates();
  const latest = useRef(refresh);
  useEffect(() => { latest.current = refresh; }, [refresh]);
  const key = areas.join('|');
  const skipWhenCovered = options?.skipWhenCovered === true;
  useEffect(
    () => subscribe(key.split('|').filter(isChangeArea), () => latest.current(), { skipWhenCovered }),
    [subscribe, key, skipWhenCovered],
  );
}

/** Run `handle` for every typed event. Without live updates it never runs. */
export function useChangeEvents(handle: (event: ChangeEvent) => void): void {
  const { subscribeEvents } = useLiveUpdates();
  const latest = useRef(handle);
  useEffect(() => { latest.current = handle; }, [handle]);
  useEffect(() => subscribeEvents(event => latest.current(event)), [subscribeEvents]);
}

/**
 * Poll `refresh` on the live-update schedule: every 10 seconds while the hub is not connected,
 * about once a minute while it is. Calling code still performs its own first load.
 */
export function usePollingRefresh(refresh: () => void, enabled = true): void {
  const { pollIntervalMs: interval } = useLiveUpdates();
  const latest = useRef(refresh);
  useEffect(() => { latest.current = refresh; }, [refresh]);
  useEffect(() => {
    if (!enabled) return;
    const timer = window.setInterval(() => latest.current(), interval);
    return () => window.clearInterval(timer);
  }, [interval, enabled]);
}
