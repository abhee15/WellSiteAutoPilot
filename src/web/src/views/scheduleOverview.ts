import type { AssetResponse, ConfiguredLogicResponse, ExecutionResponse } from '../api/generated/v1/types.gen';
import { configurationAssetLabels, latestRunByConfiguration } from './runContext';

export type ScheduleRow = {
  id: string;
  name: string;
  revisionNumber: number | null;
  mode: string | null;
  assetLabels: string[];
  enabled: boolean;
  cadenceSeconds: number;
  timeZoneId: string | null;
  /** The next occurrence of the schedule, which is when a run is due, not a promise that it starts then. */
  nextRunAtUtc: string | null;
  lastRun: { requestedAtUtc: string; status: string | null } | null;
};

export type ScheduleOverview = {
  /** Enabled schedules first, soonest first; then the ones switched off, by name. */
  rows: ScheduleRow[];
  /** Configurations that have no schedule on their live revision, so a person knows they were not forgotten. */
  unscheduledCount: number;
};

/** Every configuration whose live revision has a schedule, with when the server says it is due next and how its last run went. */
export function buildScheduleOverview(
  configurations: readonly ConfiguredLogicResponse[],
  assets: readonly AssetResponse[],
  executions: readonly Pick<ExecutionResponse, 'logicInstanceId' | 'requestedAtUtc' | 'status'>[],
): ScheduleOverview {
  const lastRuns = latestRunByConfiguration(executions);
  const rows: ScheduleRow[] = [];
  let unscheduledCount = 0;
  for (const configuration of configurations) {
    const active = configuration.revisions?.find(revision => revision.id === configuration.activeRevisionId);
    const schedule = active?.schedule;
    if (!configuration.id || !active || !schedule) {
      unscheduledCount++;
      continue;
    }
    // The server counts the cadence once, with the same code dispatch uses; a value it cannot give is shown as none.
    const nextText = configuration.nextRunAtUtc;
    const next = typeof nextText === 'string' && Number.isFinite(Date.parse(nextText)) ? Date.parse(nextText) : null;
    rows.push({
      id: configuration.id,
      name: configuration.name ?? '',
      revisionNumber: active.revisionNumber ?? null,
      mode: active.mode ?? null,
      assetLabels: configurationAssetLabels({ ...configuration, revisions: [active] }, assets),
      enabled: schedule.enabled === true,
      cadenceSeconds: schedule.cadenceSeconds ?? 0,
      timeZoneId: schedule.timeZoneId ?? null,
      nextRunAtUtc: next === null ? null : new Date(next).toISOString(),
      lastRun: lastRuns.get(configuration.id) ?? null,
    });
  }
  rows.sort((a, b) => {
    if (a.enabled !== b.enabled) return a.enabled ? -1 : 1;
    const byTime = Date.parse(a.nextRunAtUtc ?? '') - Date.parse(b.nextRunAtUtc ?? '');
    return Number.isFinite(byTime) && byTime !== 0 ? byTime : a.name.localeCompare(b.name);
  });
  return { rows, unscheduledCount };
}
