import { useCallback, useEffect, useRef, useState } from 'react';
import { useChangeRefresh, usePollingRefresh } from '../live/LiveUpdates';
import { ActionButton } from '../components/ActionButton';
import { Status } from '../components/Status';
import { t } from '../localization';
import { formatDetailTimestamp } from './executionDetail';
import { KILL_SWITCH_REASON_MAX, KillSwitchError, fetchKillSwitch, setKillSwitch } from './controlKillSwitch';
import type { KillSwitchErrorKind, KillSwitchState } from './controlKillSwitch';

type Props = {
  userId: string | null | undefined;
  /** control.manage: may engage and release. Everyone who can read recommendations sees the state. */
  canManage: boolean;
};

/**
 * The state of the control kill switch, always visible beside the recommendations it governs, with engage and
 * release for those who hold control.manage. Engaging only ever stops commands, so it needs no confirmation;
 * releasing lets them flow again, so it asks for one.
 */
export function KillSwitchPanel({ userId, canManage }: Props) {
  const [state, setState] = useState<KillSwitchState | null>(null);
  const [loadError, setLoadError] = useState<KillSwitchErrorKind | null>(null);
  const [formOpen, setFormOpen] = useState(false);
  const [reason, setReason] = useState('');
  const [acknowledged, setAcknowledged] = useState(false);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<KillSwitchErrorKind | null>(null);
  const refreshRef = useRef<AbortController | null>(null);
  const saveRef = useRef<AbortController | null>(null);

  const refresh = useCallback(async () => {
    refreshRef.current?.abort();
    const controller = new AbortController();
    refreshRef.current = controller;
    try {
      const next = await fetchKillSwitch(controller.signal);
      if (controller.signal.aborted) return;
      setState(next);
      setLoadError(null);
    } catch (failure) {
      if (controller.signal.aborted) return;
      // Never keep showing "released" when the state can no longer be confirmed.
      setState(null);
      setLoadError(failure instanceof KillSwitchError ? failure.kind : 'unavailable');
    }
  }, []);

  useEffect(() => {
    if (!userId) {
      setState(null);
      return;
    }
    void refresh();
    return () => {
      refreshRef.current?.abort();
      saveRef.current?.abort();
    };
  }, [userId, refresh]);
  usePollingRefresh(() => void refresh(), Boolean(userId));
  useChangeRefresh(['control'], () => { if (userId) void refresh(); });

  const closeForm = () => {
    setFormOpen(false);
    setReason('');
    setAcknowledged(false);
    setSaveError(null);
  };

  const submit = async () => {
    if (!state || saving) return;
    const engage = !state.engaged;
    if (!engage && !acknowledged) return;
    saveRef.current?.abort();
    const controller = new AbortController();
    saveRef.current = controller;
    setSaving(true);
    setSaveError(null);
    try {
      const next = await setKillSwitch(engage, reason, controller.signal);
      if (controller.signal.aborted) return;
      setState(next);
      closeForm();
    } catch (failure) {
      if (controller.signal.aborted) return;
      setSaveError(failure instanceof KillSwitchError ? failure.kind : 'unavailable');
      // The state may have changed under us; show what is true now.
      void refresh();
    } finally {
      if (saveRef.current === controller) {
        saveRef.current = null;
        setSaving(false);
      }
    }
  };

  const errors: Record<KillSwitchErrorKind, string> = {
    invalid: t('killSwitch.invalid'),
    accessDenied: t('killSwitch.accessDenied'),
    unavailable: t('killSwitch.unavailable'),
    invalidResponse: t('killSwitch.invalidResponse'),
  };

  if (!userId) return null;
  if (!state) {
    return (
      <section className="panel kill-switch" aria-label={t('killSwitch.heading')}>
        <div className="panel-header"><h2>{t('killSwitch.heading')}</h2></div>
        <p role={loadError ? 'alert' : 'status'} className={loadError ? 'execution-detail-error' : undefined}>
          {loadError ? errors[loadError] : t('killSwitch.loading')}
        </p>
      </section>
    );
  }

  const engaged = state.engaged;
  return (
    <section className="panel kill-switch" aria-label={t('killSwitch.heading')} data-engaged={engaged}>
      <div className="panel-header">
        <h2>{t('killSwitch.heading')}</h2>
        <Status value={engaged ? t('killSwitch.engaged') : t('killSwitch.released')} tone={engaged ? 'warning' : 'success'} />
      </div>
      <p>{engaged ? t('killSwitch.engagedNote') : t('killSwitch.releasedNote')}</p>
      {!state.autonomousControlEnabled && <p role="note">{t('killSwitch.deploymentOff')}</p>}
      {state.changedBy && (
        <p><small>{t('killSwitch.changedBy', { by: state.changedBy, at: formatDetailTimestamp(state.changedAtUtc) })}
          {state.reason ? ' ' + t('killSwitch.reason', { reason: state.reason }) : ''}</small></p>
      )}
      {canManage && !formOpen && (
        <div className="execution-detail-actions">
          <ActionButton label={engaged ? t('killSwitch.release') : t('killSwitch.engage')}
            variant={engaged ? 'secondary' : 'primary'} onClick={() => setFormOpen(true)} />
        </div>
      )}
      {canManage && formOpen && (
        <div className="kill-switch-form">
          <label className="recommendation-review-reason">
            <span>{t('killSwitch.reasonLabel')}</span>
            <textarea rows={2} maxLength={KILL_SWITCH_REASON_MAX} value={reason} disabled={saving}
              onChange={event => { setReason(event.target.value); setSaveError(null); }} />
          </label>
          {engaged && (
            <label className="recommendation-review-confirm">
              <input type="checkbox" checked={acknowledged} disabled={saving}
                onChange={event => setAcknowledged(event.target.checked)} />
              {t('killSwitch.releaseConfirmation')}
            </label>
          )}
          {saveError && <p className="execution-detail-error" role="alert">{errors[saveError]}</p>}
          {saving && <p role="status">{t('killSwitch.saving')}</p>}
          <div className="execution-detail-actions">
            <ActionButton label={t('decision.cancel')} onClick={closeForm} disabled={saving} />
            <ActionButton variant="primary" label={engaged ? t('killSwitch.confirmRelease') : t('killSwitch.confirmEngage')}
              disabled={saving || (engaged && !acknowledged)} onClick={() => void submit()} />
          </div>
        </div>
      )}
    </section>
  );
}
