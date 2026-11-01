import { useCallback, useEffect, useRef, useState } from 'react';
import { useChangeRefresh, usePollingRefresh } from '../live/LiveUpdates';
import { ActionButton } from '../components/ActionButton';
import { Status } from '../components/Status';
import { t } from '../localization';
import type { MessageKey } from '../localization';
import type { ControlActionResponse } from '../api/generated/v1/types.gen';
import { formatDetailTimestamp } from './executionDetail';
import { gateMessageKey, outcomeMessageKey } from './controlExecution';
import {
  ControlActionError, RESOLUTION_NOTE_MAX, controlActionNeedsDecision, fetchControlActions, resolveControlAction,
  verifyChangedAction, verifyControlAction,
} from './controlActions';
import type { ControlActionErrorKind } from './controlActions';

type Props = {
  userId: string | null | undefined;
  /** control.manage: may verify and resolve. Everyone who can read recommendations sees what needs a decision. */
  canManage: boolean;
};

const ERRORS: Record<ControlActionErrorKind, MessageKey> = {
  invalid: 'controlActions.invalid',
  accessDenied: 'controlActions.accessDenied',
  notFound: 'controlActions.notFound',
  conflict: 'controlActions.conflict',
  unavailable: 'controlActions.unavailable',
  invalidResponse: 'controlActions.invalidResponse',
};

/**
 * Commands that were sent but whose result nobody has confirmed. Verifying asks the target again; resolving records,
 * as the operator and with a note, whether the command was applied. Shown only while something needs a decision.
 */
export function ControlActionsPanel({ userId, canManage }: Props) {
  const [actions, setActions] = useState<ControlActionResponse[] | null>(null);
  const [loadError, setLoadError] = useState<ControlActionErrorKind | null>(null);
  const [resolvingId, setResolvingId] = useState<string | null>(null);
  const [applied, setApplied] = useState<boolean | null>(null);
  const [note, setNote] = useState('');
  const [busyId, setBusyId] = useState<string | null>(null);
  const [rowError, setRowError] = useState<{ id: string; kind: ControlActionErrorKind } | null>(null);
  const [verifyNote, setVerifyNote] = useState<{ id: string; unchanged: boolean } | null>(null);
  const refreshRef = useRef<AbortController | null>(null);
  const actRef = useRef<AbortController | null>(null);

  const refresh = useCallback(async () => {
    refreshRef.current?.abort();
    const controller = new AbortController();
    refreshRef.current = controller;
    try {
      const next = await fetchControlActions(controller.signal);
      if (controller.signal.aborted) return;
      setActions(next);
      setLoadError(null);
    } catch (failure) {
      if (controller.signal.aborted) return;
      // Never keep showing a list that can no longer be confirmed.
      setActions(null);
      setLoadError(failure instanceof ControlActionError ? failure.kind : 'unavailable');
    }
  }, []);

  useEffect(() => {
    if (!userId) {
      setActions(null);
      return;
    }
    void refresh();
    return () => {
      refreshRef.current?.abort();
      actRef.current?.abort();
    };
  }, [userId, refresh]);
  usePollingRefresh(() => void refresh(), Boolean(userId));
  useChangeRefresh(['control'], () => { if (userId) void refresh(); });

  const closeResolve = () => {
    setResolvingId(null);
    setApplied(null);
    setNote('');
  };

  const run = async (id: string, work: (signal: AbortSignal) => Promise<void>) => {
    if (busyId) return;
    actRef.current?.abort();
    const controller = new AbortController();
    actRef.current = controller;
    setBusyId(id);
    setRowError(null);
    setVerifyNote(null);
    try {
      await work(controller.signal);
    } catch (failure) {
      if (controller.signal.aborted) return;
      setRowError({ id, kind: failure instanceof ControlActionError ? failure.kind : 'unavailable' });
      // Someone else may have settled it; show what is true now.
      void refresh();
    } finally {
      if (actRef.current === controller) {
        actRef.current = null;
        setBusyId(null);
      }
    }
  };

  const verify = (action: ControlActionResponse) => run(action.id, async signal => {
    const after = await verifyControlAction(action.id, signal);
    if (signal.aborted) return;
    setVerifyNote({ id: action.id, unchanged: !verifyChangedAction(action, after) });
    await refresh();
  });

  const resolve = (action: ControlActionResponse) => run(action.id, async signal => {
    if (applied === null) return;
    await resolveControlAction(action.id, applied, note, signal);
    if (signal.aborted) return;
    closeResolve();
    await refresh();
  });

  if (!userId) return null;
  if (loadError) {
    return (
      <section className="panel control-actions" aria-label={t('controlActions.heading')}>
        <div className="panel-header"><h2>{t('controlActions.heading')}</h2></div>
        <p role="alert" className="execution-detail-error">{t(ERRORS[loadError])}</p>
      </section>
    );
  }
  const open = (actions ?? []).filter(controlActionNeedsDecision);
  if (open.length === 0) return null;

  return (
    <section className="panel control-actions" aria-label={t('controlActions.heading')}>
      <div className="panel-header">
        <h2>{t('controlActions.heading')}</h2>
        <Status value={t('controlActions.count', { count: open.length })} tone="warning" />
      </div>
      <p>{t('controlActions.intro')}</p>
      <ul className="control-actions-list">
        {open.map(action => {
          const outcome = outcomeMessageKey(action.status);
          const gate = gateMessageKey(action.gateCode);
          const resolving = resolvingId === action.id;
          const busy = busyId === action.id;
          return (
            <li key={action.id} data-status={action.status}>
              <p>
                <strong>{action.command}</strong>
                {action.value !== null && action.value !== undefined
                  ? ` ${action.value}${action.unit ? ' ' + action.unit : ''}` : ''}
                {' · '}{t('controlActions.sentAt', { at: formatDetailTimestamp(action.createdAtUtc) })}
              </p>
              <p>{outcome ? t(outcome) : action.status}{gate ? ' ' + t(gate) : ''}</p>
              {verifyNote?.id === action.id && (
                <p role="status">{t(verifyNote.unchanged ? 'controlActions.verifyUnchanged' : 'controlActions.verifyChanged')}</p>
              )}
              {rowError?.id === action.id && (
                <p className="execution-detail-error" role="alert">{t(ERRORS[rowError.kind])}</p>
              )}
              {canManage && !resolving && (
                <div className="execution-detail-actions">
                  <ActionButton label={t('controlActions.verify')} aria-label={t('controlActions.verifyFor', { command: action.command })}
                    disabled={busyId !== null} busy={busy} onClick={() => void verify(action)} />
                  <ActionButton label={t('controlActions.resolve')} aria-label={t('controlActions.resolveFor', { command: action.command })}
                    disabled={busyId !== null}
                    onClick={() => { setResolvingId(action.id); setApplied(null); setNote(''); setRowError(null); setVerifyNote(null); }} />
                </div>
              )}
              {canManage && resolving && (
                <div className="control-actions-resolve" role="group" aria-label={t('controlActions.resolveFor', { command: action.command })}>
                  <p>{t('controlActions.resolveWarning')}</p>
                  <label className="recommendation-review-confirm">
                    <input type="radio" name={`applied-${action.id}`} checked={applied === true} disabled={busy}
                      onChange={() => setApplied(true)} />
                    {t('controlActions.wasApplied')}
                  </label>
                  <label className="recommendation-review-confirm">
                    <input type="radio" name={`applied-${action.id}`} checked={applied === false} disabled={busy}
                      onChange={() => setApplied(false)} />
                    {t('controlActions.wasNotApplied')}
                  </label>
                  <label className="recommendation-review-reason">
                    <span>{t('controlActions.noteLabel')}</span>
                    <textarea rows={2} maxLength={RESOLUTION_NOTE_MAX} value={note} disabled={busy}
                      onChange={event => { setNote(event.target.value); setRowError(null); }} />
                  </label>
                  <div className="execution-detail-actions">
                    <ActionButton label={t('decision.cancel')} disabled={busy} onClick={closeResolve} />
                    <ActionButton variant="primary" label={t('controlActions.confirmResolve')} busy={busy}
                      disabled={busy || applied === null || note.trim().length === 0} onClick={() => void resolve(action)} />
                  </div>
                </div>
              )}
            </li>
          );
        })}
      </ul>
    </section>
  );
}
