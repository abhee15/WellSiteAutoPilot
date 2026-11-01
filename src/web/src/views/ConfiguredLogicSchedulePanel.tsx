import { useCallback, useEffect, useRef, useState } from 'react';
import { ActionButton } from '../components/ActionButton';
import { LoadingIndicator } from '../components/LoadingIndicator';
import { formatTimestamp } from '../format';
import { t } from '../localization';
import type { MessageKey } from '../localization';
import { formatDuration } from './executionDetail';
import { SCHEDULE_REASON_MAX, ScheduleControlError, canResume, changeSchedule, fetchScheduleControl, scheduleRunState } from './scheduleControl';
import type { ScheduleChange, ScheduleControl, ScheduleControlErrorKind } from './scheduleControl';
import { useLiveResource } from './useLiveResource';

type Props = {
  configuredLogicId: string;
  /** The live revision the page is showing; a different live revision makes this view stale. */
  activeRevisionId: string | null | undefined;
  /** configured-logic.manage on this configuration; the server decides again on every change. */
  mayManage: boolean;
  /** A revision is being edited, a change is in flight, or the live revision changed elsewhere. */
  disabled: boolean;
};

type Form = 'frequency' | 'resume' | null;

const KIND_KEY: Record<ScheduleChange['kind'], MessageKey> = {
  Paused: 'scheduleControl.kindPaused',
  Resumed: 'scheduleControl.kindResumed',
  FrequencyChanged: 'scheduleControl.kindFrequency',
  Reset: 'scheduleControl.kindReset',
};

const interval = (seconds: number | null) => seconds === null ? '—' : formatDuration(seconds * 1000);

function changeText(change: ScheduleChange): string {
  const detail = change.kind === 'FrequencyChanged' && change.schedule.cadenceSeconds !== null
    ? ' ' + t('scheduleControl.toEvery', { interval: interval(change.schedule.cadenceSeconds) }) : '';
  return `${formatTimestamp(change.changedAtUtc)} · ${change.changedBy} · ${t(KIND_KEY[change.kind])}${detail}` +
    (change.reason ? ` · “${change.reason}”` : '');
}

/**
 * Pause, resume and change the frequency of the live schedule. None of these create a revision: the saved revision
 * keeps what it declares and the changes are recorded beside it. Pausing only ever stops runs, so it asks for no
 * confirmation; resuming and a new frequency start runs again, so they ask.
 */
export function ConfiguredLogicSchedulePanel({ configuredLogicId, activeRevisionId, mayManage, disabled }: Props) {
  const load = useCallback((signal: AbortSignal) => fetchScheduleControl(configuredLogicId, signal), [configuredLogicId]);
  const live = useLiveResource(`${configuredLogicId}:schedule`, load, ['configured-logic'], Boolean(activeRevisionId));
  const [saved, setSaved] = useState<ScheduleControl | null>(null);
  const [form, setForm] = useState<Form>(null);
  const [reason, setReason] = useState('');
  const [seconds, setSeconds] = useState('');
  const [acknowledged, setAcknowledged] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<ScheduleControlErrorKind | null>(null);
  const request = useRef<AbortController | null>(null);
  useEffect(() => () => request.current?.abort(), []);

  // A change's own answer is shown at once; a later live read replaces it.
  const control = saved ?? live.data ?? null;
  useEffect(() => { if (live.data) setSaved(null); }, [live.data]);

  const close = () => { setForm(null); setReason(''); setSeconds(''); setAcknowledged(false); setError(null); };
  const run = async (change: Parameters<typeof changeSchedule>[1]) => {
    if (!control || saving) return;
    request.current?.abort();
    const controller = new AbortController();
    request.current = controller;
    setSaving(true); setError(null);
    try {
      const next = await changeSchedule(control, change, reason, controller.signal);
      if (controller.signal.aborted) return;
      setSaved(next); close(); live.refresh();
    } catch (failure) {
      if (controller.signal.aborted) return;
      setError(failure instanceof ScheduleControlError ? failure.kind : 'unavailable');
      live.refresh();
    } finally {
      if (request.current === controller) { request.current = null; setSaving(false); }
    }
  };

  const errors: Record<ScheduleControlErrorKind, string> = {
    invalid: t('scheduleControl.invalid'),
    accessDenied: t('scheduleControl.accessDenied'),
    stale: t('scheduleControl.stale'),
    unavailable: t('scheduleControl.unavailable'),
    invalidResponse: t('scheduleControl.invalidResponse'),
  };
  const heading = <h4>{t('scheduleControl.heading')}</h4>;
  if (!activeRevisionId) return null;
  if (live.denied) return null;
  if (!control) {
    return <section className="logic-detail-section" aria-label={t('scheduleControl.heading')}>
      {heading}
      {live.loading && <LoadingIndicator label={t('scheduleControl.loading')} />}
      {live.failed && <p role="alert">{t('scheduleControl.unavailable')} <ActionButton variant="quiet" label={t('assetDetail.retry')} onClick={live.refresh} /></p>}
    </section>;
  }

  const state = scheduleRunState(control);
  const manage = mayManage && control.canManage && state !== 'none';
  const busy = disabled || saving;
  const parsed = Number(seconds);
  const cadenceValid = seconds.trim() !== '' && Number.isInteger(parsed) &&
    parsed >= control.minimumCadenceSeconds && parsed <= control.maximumCadenceSeconds;
  const latest = control.changes[0];
  const summary = state === 'none' ? t('scheduleControl.none')
    : state === 'paused'
      ? t('scheduleControl.pausedBy', { by: latest?.kind === 'Paused' ? latest.changedBy : '—' }) +
        (latest?.kind === 'Paused' && latest.reason ? ` “${latest.reason}”.` : '')
      : t('scheduleControl.running', { interval: interval(control.effective?.cadenceSeconds ?? null) });
  const next = state === 'running'
    ? `${t('logicDetail.nextScheduled')}: ${control.nextRunAtUtc ? formatTimestamp(control.nextRunAtUtc) : t('logicDetail.notAvailable')}`
    : state === 'paused' ? `${t('logicDetail.nextScheduled')}: ${t('scheduleControl.nextNone')}` : null;

  return <section className="logic-detail-section schedule-control" aria-label={t('scheduleControl.heading')}
    data-state={state} data-differs={control.differsFromRevision}>
    {heading}
    <p>{summary}{next && <> {next}</>}</p>
    <p className="logic-detail-note">{control.differsFromRevision
      ? t('scheduleControl.differs', { interval: interval(control.declared?.cadenceSeconds ?? null) })
      : t('scheduleControl.matches')} {t('scheduleControl.noRevision')}</p>
    {manage && form === null && <div className="setup-actions">
      <ActionButton label={t('scheduleControl.changeFrequency')} disabled={busy} onClick={() => { setError(null); setSeconds(String(control.effective?.cadenceSeconds ?? '')); setForm('frequency'); }} />
      {state === 'running'
        ? <ActionButton variant="primary" label={t('scheduleControl.pause')} busy={saving} disabled={busy} onClick={() => void run({ kind: 'pause' })} />
        : <ActionButton variant="primary" label={t('scheduleControl.resume')} disabled={busy || !canResume(control)} onClick={() => { setError(null); setForm('resume'); }} />}
    </div>}
    {manage && form !== null && <div className="schedule-control-form">
      {form === 'frequency' && <label>
        <span>{t('scheduleControl.secondsLabel', { minimum: control.minimumCadenceSeconds, maximum: control.maximumCadenceSeconds })}</span>
        <input type="number" inputMode="numeric" min={control.minimumCadenceSeconds} max={control.maximumCadenceSeconds} step={1}
          value={seconds} disabled={busy} onChange={event => { setSeconds(event.target.value); setError(null); }} />
      </label>}
      {form === 'frequency' && cadenceValid && <p className="logic-detail-note">{t('scheduleControl.preview', { interval: interval(parsed) })}</p>}
      {form === 'resume' && <>
        <p>{t('scheduleControl.resumeNote', { interval: interval(control.effective?.cadenceSeconds ?? null) })}</p>
        <label className="recommendation-review-confirm">
          <input type="checkbox" checked={acknowledged} disabled={busy} onChange={event => setAcknowledged(event.target.checked)} />
          {t('scheduleControl.resumeConfirmation')}
        </label>
      </>}
      <label>
        <span>{t('scheduleControl.reasonLabel')}</span>
        <textarea rows={2} maxLength={SCHEDULE_REASON_MAX} value={reason} disabled={busy}
          onChange={event => { setReason(event.target.value); setError(null); }} />
      </label>
      <div className="setup-actions">
        <ActionButton label={t('decision.cancel')} disabled={saving} onClick={close} />
        <ActionButton variant="primary" busy={saving}
          label={form === 'resume' ? t('scheduleControl.confirmResume') : t('scheduleControl.confirmFrequency')}
          disabled={disabled || (form === 'resume' ? !acknowledged : !cadenceValid)}
          onClick={() => void run(form === 'resume' ? { kind: 'resume' } : { kind: 'frequency', cadenceSeconds: parsed })} />
      </div>
    </div>}
    {error && <p className="execution-detail-error" role="alert">{errors[error]}</p>}
    {saving && <p role="status">{t('scheduleControl.saving')}</p>}
    {control.changes.length > 0 && <details>
      <summary>{t('scheduleControl.changes', { count: control.changes.length })}</summary>
      <ul className="asset-page-list">{control.changes.map(change => <li key={change.id}>{changeText(change)}</li>)}</ul>
    </details>}
  </section>;
}
