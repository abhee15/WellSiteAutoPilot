import { useEffect, useId, useState } from 'react';
import { CheckboxField, NumberField, SelectField, TextField } from '../components/FormFields';
import { Notice } from '../components/Notice';
import { t } from '../localization';
import { SCHEDULE_UNITS, buildAssignmentSchedule, scheduleErrorKeys, scheduleUnitKeys, timeZoneChoices } from './assignmentSchedule';
import type { ScheduleDraft } from './assignmentSchedule';
import { setupResponse } from './configuredLogicSetup';

export function AssignmentScheduleEditor({ value, onChange }: {
  value: ScheduleDraft; onChange: (value: ScheduleDraft) => void;
}) {
  const repeatId = useId();
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
    <SelectField label={t('assignment.runWhen')} value={value.scheduled ? 'scheduled' : 'manual'}
      onChange={event => change({ scheduled: event.target.value === 'scheduled' })}>
      <option value="manual">{t('assignment.onDemand')}</option><option value="scheduled">{t('assignment.scheduled')}</option>
    </SelectField>
    {value.scheduled && <>
      <div className="assignment-schedule-fields">
        <TextField label={t('assignment.startDate')} type="date" required value={value.date} onChange={event => change({ date: event.target.value })} />
        <TextField label={t('assignment.startTime')} type="time" step="1" required value={value.time} onChange={event => change({ time: event.target.value })} />
        <SelectField label={t('assignment.timeZone')} value={value.timeZone} onChange={event => change({ timeZone: event.target.value })}>
          {timeZoneChoices(value.timeZone).map(zone => <option key={zone} value={zone}>{zone}</option>)}
        </SelectField>
        <div className="assignment-repeat" role="group" aria-labelledby={repeatId}>
          <span id={repeatId} className="ui-field-label">{t('assignment.repeatEvery')}</span>
          <div className="assignment-frequency">
            <NumberField label={t('assignment.interval')} min="1" max="999" step="1" required value={value.every}
              onChange={event => change({ every: event.target.value })} />
            <SelectField label={t('assignment.unit')} value={value.unit} onChange={event => change({ unit: event.target.value })}>
              {SCHEDULE_UNITS.map(unit => <option key={unit} value={unit}>{t(scheduleUnitKeys[unit])}</option>)}
            </SelectField>
          </div>
        </div>
      </div>
      <CheckboxField label={t('assignment.setEnd')} checked={value.hasEnd} onChange={event => change({ hasEnd: event.target.checked })} />
      {value.hasEnd && <TextField label={t('assignment.endDate')} type="date" required min={value.date} value={value.endDate}
        helper={t('assignment.endHint')} onChange={event => change({ endDate: event.target.value })} />}
      {value.unit === 'Months' && <SelectField label={t('assignment.shortMonth')} value={value.skipMissingMonthDay ? 'skip' : 'last'}
        onChange={event => change({ skipMissingMonthDay: event.target.value === 'skip' })}>
        <option value="last">{t('assignment.lastDay')}</option><option value="skip">{t('assignment.skipMonth')}</option>
      </SelectField>}
      <p className="setup-step-hint">{t(['Days', 'Weeks', 'Months'].includes(value.unit) ? 'assignment.calendarHint' : 'assignment.intervalHint')}</p>
      {built.error && <Notice tone="error">{t(scheduleErrorKeys[built.error])}</Notice>}
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
