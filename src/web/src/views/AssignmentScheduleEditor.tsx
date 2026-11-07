import { useEffect, useState } from 'react';
import { t } from '../localization';
import { SCHEDULE_UNITS, buildAssignmentSchedule, scheduleErrorKeys, scheduleUnitKeys, timeZoneChoices } from './assignmentSchedule';
import type { ScheduleDraft } from './assignmentSchedule';
import { setupResponse } from './configuredLogicSetup';

export function AssignmentScheduleEditor({ value, onChange }: {
  value: ScheduleDraft; onChange: (value: ScheduleDraft) => void;
}) {
  const built = buildAssignmentSchedule(value);
  const key = JSON.stringify(built.schedule);
  const [preview, setPreview] = useState<{ key: string; times: string[]; error: boolean } | null>(null);
  useEffect(() => {
    if (key === 'null') return;
    const request = new AbortController();
    const timer = window.setTimeout(() => {
      void fetch('/api/v1/configured-logic/schedule-preview', { method: 'POST', credentials: 'same-origin',
        signal: request.signal, headers: { 'Content-Type': 'application/json' }, body: key })
        .then(response => setupResponse(response, 200, false)).then(body => {
          if (!Array.isArray(body) || body.some(item => typeof item !== 'string' || !Number.isFinite(Date.parse(item)))) throw new Error();
          if (!request.signal.aborted) setPreview({ key, times: body, error: false });
        }).catch(() => { if (!request.signal.aborted) setPreview({ key, times: [], error: true }); });
    }, 250);
    return () => { window.clearTimeout(timer); request.abort(); };
  }, [key]);
  const change = (patch: Partial<ScheduleDraft>) => onChange({ ...value, ...patch });
  return <section className="assignment-schedule" aria-label={t('setupStep.schedule')}>
    <h4>{t('setupStep.schedule')}</h4>
    <label>{t('assignment.runWhen')}<select value={value.scheduled ? 'scheduled' : 'manual'}
      onChange={event => change({ scheduled: event.target.value === 'scheduled' })}>
      <option value="manual">{t('assignment.onDemand')}</option><option value="scheduled">{t('assignment.scheduled')}</option>
    </select></label>
    {value.scheduled && <>
      <div className="assignment-schedule-fields">
        <label>{t('assignment.startDate')}<input type="date" required value={value.date} onChange={event => change({ date: event.target.value })} /></label>
        <label>{t('assignment.startTime')}<input type="time" step="1" required value={value.time} onChange={event => change({ time: event.target.value })} /></label>
        <label>{t('assignment.timeZone')}<select value={value.timeZone} onChange={event => change({ timeZone: event.target.value })}>
          {timeZoneChoices(value.timeZone).map(zone => <option key={zone} value={zone}>{zone}</option>)}
        </select></label>
        <label>{t('assignment.repeatEvery')}<span className="assignment-frequency">
          <input type="number" min="1" max="999" step="1" required value={value.every}
            aria-label={t('assignment.interval')} onChange={event => change({ every: event.target.value })} />
          <select value={value.unit} aria-label={t('assignment.unit')} onChange={event => change({ unit: event.target.value })}>
            {SCHEDULE_UNITS.map(unit => <option key={unit} value={unit}>{t(scheduleUnitKeys[unit])}</option>)}
          </select>
        </span></label>
      </div>
      <label className="assignment-checkbox"><input type="checkbox" checked={value.hasEnd}
        onChange={event => change({ hasEnd: event.target.checked })} />{t('assignment.setEnd')}</label>
      {value.hasEnd && <label>{t('assignment.endDate')}<input required type="date" aria-label={t('assignment.endDate')} min={value.date} value={value.endDate}
        onChange={event => change({ endDate: event.target.value })} /><small>{t('assignment.endHint')}</small></label>}
      {value.unit === 'Months' && <label>{t('assignment.shortMonth')}<select value={value.skipMissingMonthDay ? 'skip' : 'last'}
        onChange={event => change({ skipMissingMonthDay: event.target.value === 'skip' })}>
        <option value="last">{t('assignment.lastDay')}</option><option value="skip">{t('assignment.skipMonth')}</option>
      </select></label>}
      <p className="setup-step-hint">{t(['Days', 'Weeks', 'Months'].includes(value.unit) ? 'assignment.calendarHint' : 'assignment.intervalHint')}</p>
      {built.error && <p role="alert">{t(scheduleErrorKeys[built.error])}</p>}
      {!built.error && <details open><summary>{t('assignment.nextStarts')}</summary>
        {preview?.key === key && !preview.error
          ? preview.times.length ? <ul>{preview.times.map(time => <li key={time}>{new Intl.DateTimeFormat(undefined, {
            timeZone: value.timeZone, dateStyle: 'medium', timeStyle: 'medium' }).format(new Date(time))}</li>)}</ul>
            : <p>{t('assignment.noStarts')}</p>
          : <p>{t(preview?.key === key && preview.error ? 'assignment.previewError' : 'assignment.previewLoading')}</p>}
        <small>{value.timeZone}</small>
      </details>}
    </>}
  </section>;
}
