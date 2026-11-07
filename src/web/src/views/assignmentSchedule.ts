import type { ConfiguredLogicScheduleRequest, ConfiguredLogicScheduleResponse } from '../api/generated/v1/types.gen';

export type ScheduleDraft = {
  scheduled: boolean; enabled: boolean; date: string; time: string; timeZone: string;
  every: string; unit: string; hasEnd: boolean; endDate: string; skipMissingMonthDay: boolean;
};
export const scheduleErrorKeys = {
  interval: 'assignment.scheduleError.interval', start: 'assignment.scheduleError.start', end: 'assignment.scheduleError.end',
} as const;
export const scheduleUnitKeys: Record<string, 'assignment.unitSeconds' | 'assignment.unitMinutes' | 'assignment.unitHours' |
  'assignment.unitDays' | 'assignment.unitWeeks' | 'assignment.unitMonths'> = {
  Seconds: 'assignment.unitSeconds', Minutes: 'assignment.unitMinutes', Hours: 'assignment.unitHours',
  Days: 'assignment.unitDays', Weeks: 'assignment.unitWeeks', Months: 'assignment.unitMonths',
};
export const SCHEDULE_UNITS = ['Seconds', 'Minutes', 'Hours', 'Days', 'Weeks', 'Months'] as const;
const factors: Record<string, number> = { Seconds: 1, Minutes: 60, Hours: 3600, Days: 86400, Weeks: 604800, Months: 2419200 };

function wallTime(instant: Date, timeZone: string): string {
  const parts = Object.fromEntries(new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit',
    day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23' })
    .formatToParts(instant).map(part => [part.type, part.value]));
  return `${parts.year}-${parts.month}-${parts.day}T${parts.hour}:${parts.minute}:${parts.second}`;
}

/** Pick the earlier instant for a repeated local time and reject a missing local time. */
export function localScheduleInstant(date: string, time: string, timeZone: string): string | null {
  if (!date || !time) return null;
  const local = date + 'T' + (time.length === 5 ? time + ':00' : time);
  const wall = Date.parse(local + 'Z');
  if (!Number.isFinite(wall)) return null;
  try {
    const offsets = [-86400000, 0, 86400000].map(delta => {
      const point = new Date(wall + delta);
      return Date.parse(wallTime(point, timeZone) + 'Z') - point.getTime();
    });
    const candidates = [...new Set(offsets)].map(offset => new Date(wall - offset))
      .filter(instant => wallTime(instant, timeZone) === local).sort((a, b) => a.getTime() - b.getTime());
    return candidates[0]?.toISOString() ?? null;
  } catch { return null; }
}

/**
 * Every time zone the browser knows, led by the zone already chosen (kept even if the browser does not list it),
 * the browser's own zone and UTC, which the browser's list leaves out. No zone is named in the app itself.
 */
export function timeZoneChoices(current: string): string[] {
  const known = typeof Intl.supportedValuesOf === 'function' ? Intl.supportedValuesOf('timeZone') : [];
  const browser = Intl.DateTimeFormat().resolvedOptions().timeZone;
  return [...new Set([current, browser, 'UTC', ...known])].filter(Boolean);
}

export function initialScheduleDraft(schedule?: ConfiguredLogicScheduleResponse | null): ScheduleDraft {
  const timeZone = schedule?.timeZoneId || Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
  const start = new Date(schedule?.startAtUtc ?? Date.now() + 60_000);
  const local = wallTime(start, timeZone);
  const seconds = schedule?.cadenceSeconds ?? 3600;
  const unit = schedule?.unit ?? [...SCHEDULE_UNITS].reverse().find(candidate =>
    candidate !== 'Months' && seconds % factors[candidate] === 0) ?? 'Seconds';
  return { scheduled: Boolean(schedule), enabled: schedule?.enabled ?? true, date: local.slice(0, 10),
    time: local.slice(11), timeZone, every: String(schedule?.every ?? seconds / factors[unit]), unit,
    hasEnd: Boolean(schedule?.endAtUtc), endDate: schedule?.endAtUtc ? wallTime(new Date(schedule.endAtUtc), timeZone).slice(0, 10) : '',
    skipMissingMonthDay: schedule?.skipMissingMonthDay ?? false };
}

export function buildAssignmentSchedule(draft: ScheduleDraft): { schedule: ConfiguredLogicScheduleRequest | null; error: 'interval' | 'start' | 'end' | null } {
  if (!draft.scheduled) return { schedule: null, error: null };
  const every = Number(draft.every);
  if (!Number.isInteger(every) || every < 1 || every > 999 || !factors[draft.unit]) return { schedule: null, error: 'interval' };
  const cadenceSeconds = every * factors[draft.unit];
  if (cadenceSeconds > 2147483647) return { schedule: null, error: 'interval' };
  const startAtUtc = localScheduleInstant(draft.date, draft.time, draft.timeZone);
  if (!startAtUtc) return { schedule: null, error: 'start' };
  let endAtUtc: string | null = null;
  if (draft.hasEnd) {
    if (!draft.endDate || draft.endDate < draft.date) return { schedule: null, error: 'end' };
    const next = new Date(draft.endDate + 'T12:00:00Z');
    next.setUTCDate(next.getUTCDate() + 1);
    const midnight = localScheduleInstant(next.toISOString().slice(0, 10), '00:00', draft.timeZone);
    if (!midnight) return { schedule: null, error: 'end' };
    endAtUtc = new Date(Date.parse(midnight) - 1).toISOString();
  }
  return { error: null, schedule: { enabled: draft.enabled, cadenceSeconds, startAtUtc,
    timeZoneId: draft.timeZone, every, unit: draft.unit, endAtUtc, skipMissingMonthDay: draft.skipMissingMonthDay } };
}
