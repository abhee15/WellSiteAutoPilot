import { useCallback, useEffect, useReducer, useRef, useState } from 'react';
import { Bell, CircleX, Info, TriangleAlert, X } from 'lucide-react';
import { ActionButton } from './ActionButton';
import { t } from '../localization';
import { useChangeEvents, useLiveUpdates } from '../live/LiveUpdates';
import {
  EMPTY_NOTIFICATIONS, notificationFor, notificationReducer, resolvedNotificationId,
} from '../live/notifications';
import type { AppNotification } from '../live/notifications';
import type { ChangeEvent } from '../live/changeSignal';
import './notifications.css';

export const TOAST_MS = 8_000;

function describe(item: AppNotification): string {
  if (item.kind === 'decision-needed') return t('notifications.decisionNeeded');
  return item.code ? t('notifications.runFailedWithCode', { code: item.code }) : t('notifications.runFailed');
}

function severityLabel(severity: AppNotification['severity']): string {
  return severity === 'critical' ? t('notifications.critical') : severity === 'warning' ? t('notifications.warning') : t('notifications.info');
}

const SEVERITY_ICON = { critical: CircleX, warning: TriangleAlert, info: Info } as const;

/**
 * Bell, list and toast for events worth interrupting someone for. It is client-side and per browser
 * session: nothing is stored, so what happened while no page was open is not shown (see the design).
 * Nothing renders until live updates are asked for, so a view without the channel looks as before.
 */
export function NotificationCenter({ permissions }: { permissions: readonly string[] }) {
  const { state } = useLiveUpdates();
  const [notifications, dispatch] = useReducer(notificationReducer, EMPTY_NOTIFICATIONS);
  const [open, setOpen] = useState(false);
  const latestPermissions = useRef(permissions);
  useEffect(() => { latestPermissions.current = permissions; }, [permissions]);

  useChangeEvents(useCallback((event: ChangeEvent) => {
    const resolved = resolvedNotificationId(event);
    if (resolved) dispatch({ type: 'resolve', id: resolved });
    const item = notificationFor(event, latestPermissions.current);
    if (item) dispatch({ type: 'add', item });
  }, []));

  const toast = notifications.items.find(item => item.id === notifications.toastId) ?? null;
  const toastId = toast?.id ?? null;
  useEffect(() => {
    if (!toastId) return;
    const timer = window.setTimeout(() => dispatch({ type: 'toast-done', id: toastId }), TOAST_MS);
    return () => window.clearTimeout(timer);
  }, [toastId]);

  if (state === 'idle') return null;
  const unread = notifications.items.filter(item => !item.read).length;
  const label = unread > 0 ? t('notifications.openWithCount', { count: unread }) : t('notifications.open');
  return (
    <div className="notification-center">
      <ActionButton icon={Bell} iconOnly label={label} aria-expanded={open}
        aria-controls="notification-list" data-unread={unread > 0 || undefined}
        onClick={() => { setOpen(current => !current); dispatch({ type: 'mark-read' }); }} />
      {unread > 0 && <span className="notification-count" aria-hidden="true">{unread}</span>}
      {open && (
        <section id="notification-list" className="notification-list" aria-label={t('notifications.title')}>
          {notifications.items.length === 0
            ? <p className="notification-empty">{t('notifications.empty')}</p>
            : (
              <>
                <ul>
                  {notifications.items.map(item => {
                    const Icon = SEVERITY_ICON[item.severity];
                    return (
                      <li key={item.id} data-severity={item.severity}>
                        <Icon size={16} aria-hidden="true" />
                        <span className="notification-text">
                          <span className="visually-hidden">{severityLabel(item.severity)}: </span>
                          {describe(item)}
                        </span>
                        <a href={item.href} onClick={() => setOpen(false)}>{t('notifications.view')}</a>
                        <ActionButton icon={X} iconOnly variant="quiet" label={t('notifications.dismiss')}
                          onClick={() => dispatch({ type: 'dismiss', id: item.id })} />
                      </li>
                    );
                  })}
                </ul>
                <ActionButton label={t('notifications.dismissAll')} onClick={() => dispatch({ type: 'dismiss-all' })} />
              </>
            )}
        </section>
      )}
      <div className="notification-toast-region" aria-live="polite">
        {toast && !open && (
          <div className="notification-toast" data-severity={toast.severity}>
            <span>{describe(toast)}</span>
            <a href={toast.href} onClick={() => dispatch({ type: 'toast-done', id: toast.id })}>{t('notifications.view')}</a>
          </div>
        )}
      </div>
    </div>
  );
}
