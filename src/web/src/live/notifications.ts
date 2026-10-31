import { hashFor } from '../routing';
import type { ChangeEvent } from './changeSignal';

export type NotificationSeverity = 'critical' | 'warning' | 'info';
export type NotificationKind = 'run-failed' | 'decision-needed';

export type AppNotification = {
  /** Stable per entity and kind, so the same event never shows twice. */
  id: string;
  kind: NotificationKind;
  severity: NotificationSeverity;
  entityId: string;
  /** Hash route the existing router opens. */
  href: string;
  code: string | null;
  at: string;
  read: boolean;
};

export const MAX_NOTIFICATIONS = 50;

/** What an event means to this person, or null when it is not for them. */
export function notificationFor(event: ChangeEvent, permissions: readonly string[]): AppNotification | null {
  if (event.entity === 'execution' && event.kind === 'failed' && permissions.includes('executions.read')) {
    return {
      id: `run-failed:${event.id}`, kind: 'run-failed', severity: 'warning', entityId: event.id,
      href: hashFor('operations', event.id), code: event.payload.code ?? null, at: event.occurredAt, read: false,
    };
  }
  if (event.entity === 'recommendation' && event.kind === 'created' && permissions.includes('recommendations.decide')) {
    return {
      id: `decision-needed:${event.id}`, kind: 'decision-needed', severity: 'warning', entityId: event.id,
      href: hashFor('recommendations', event.id), code: null, at: event.occurredAt, read: false,
    };
  }
  return null;
}

/** An item that asks for a decision goes away once anyone decides it. */
export function resolvedNotificationId(event: ChangeEvent): string | null {
  return event.entity === 'recommendation' && event.kind === 'decided' ? `decision-needed:${event.id}` : null;
}

export type NotificationState = { items: readonly AppNotification[]; toastId: string | null };
export const EMPTY_NOTIFICATIONS: NotificationState = { items: [], toastId: null };

export type NotificationAction =
  | { type: 'add'; item: AppNotification }
  | { type: 'resolve'; id: string }
  | { type: 'dismiss'; id: string }
  | { type: 'dismiss-all' }
  | { type: 'mark-read' }
  | { type: 'toast-done'; id: string };

export function notificationReducer(state: NotificationState, action: NotificationAction): NotificationState {
  switch (action.type) {
    case 'add':
      if (state.items.some(item => item.id === action.item.id)) return state;
      return { items: [action.item, ...state.items].slice(0, MAX_NOTIFICATIONS), toastId: action.item.id };
    case 'resolve': case 'dismiss':
      return { items: state.items.filter(item => item.id !== action.id), toastId: state.toastId === action.id ? null : state.toastId };
    case 'dismiss-all':
      return EMPTY_NOTIFICATIONS;
    case 'mark-read':
      return state.items.some(item => !item.read) ? { ...state, items: state.items.map(item => ({ ...item, read: true })) } : state;
    case 'toast-done':
      return state.toastId === action.id ? { ...state, toastId: null } : state;
  }
}
