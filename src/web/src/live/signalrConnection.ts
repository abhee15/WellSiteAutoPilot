import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { HUB_PATH } from './changeSignal';
import type { HubConnectionLike } from './changeSignal';

/**
 * The real SignalR connection. It uses the same origin and the browser's own credentials as the
 * REST calls (no token handling here), keeps the library silent (a missing hub is a normal
 * state that degrades to polling), and retries a lost connection quickly before handing the
 * slow retries back to ChangeSignalClient.
 */
export function createSignalrConnection(): HubConnectionLike {
  return new HubConnectionBuilder()
    .withUrl(HUB_PATH)
    .withAutomaticReconnect([0, 2_000, 5_000, 10_000, 30_000])
    .configureLogging(LogLevel.None)
    .build();
}
