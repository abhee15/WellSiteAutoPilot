import type { ConfiguredLogicResponse, ExecutionResponse } from '../api/generated/v1/types.gen';
import { formatTimestamp } from '../format';
import { statusLabel } from '../words';
import { t } from '../localization';
import { formatDuration } from './executionDetail';

type RunLike = Pick<ExecutionResponse, 'executionId' | 'logicInstanceId' | 'requestedAtUtc' | 'status'>;

/** Newest runs of one configuration from the runs already loaded. */
export function recentRunsFor<T extends RunLike>(
  executions: readonly T[],
  configurationId: string | null | undefined,
  maximum = 5,
): T[] {
  if (!configurationId) return [];
  return executions
    .filter(run => run.logicInstanceId === configurationId && Number.isFinite(Date.parse(run.requestedAtUtc ?? '')))
    .sort((a, b) => Date.parse(b.requestedAtUtc ?? '') - Date.parse(a.requestedAtUtc ?? ''))
    .slice(0, maximum);
}

/**
 * One sentence on where this logic stands. Recorded runs are described as recorded,
 * never as having changed anything on the equipment.
 */
export function logicStatusSentence(
  logic: ConfiguredLogicResponse,
  lastRun?: Pick<RunLike, 'requestedAtUtc' | 'status'>,
): string {
  const revisions = logic.revisions ?? [];
  if (revisions.length === 0) return 'No revision has been saved yet.';
  const active = revisions.find(revision => revision.id === logic.activeRevisionId);
  if (!active) {
    const newest = [...revisions].sort((a, b) => (b.revisionNumber ?? 0) - (a.revisionNumber ?? 0))[0];
    return `Not live yet. Revision ${newest.revisionNumber ?? '—'} is ${statusLabel(newest.status).toLowerCase()}.`;
  }
  const assets = new Set((active.assetBindings ?? []).flatMap(binding => (binding.assetId ? [binding.assetId] : [])));
  const where = assets.size === 1 ? 'on 1 asset' : `on ${assets.size} assets`;
  const mode = active.mode ? ` in ${active.mode} mode` : '';
  const base = `Revision ${active.revisionNumber ?? '—'} is live${mode} ${where}.`;
  return lastRun
    ? `${base} Last recorded run ${formatTimestamp(lastRun.requestedAtUtc)}: ${statusLabel(lastRun.status).toLowerCase()}.`
    : `${base} No recorded run yet.`;
}

/** "Every 1 min · America/Chicago", or "Off" when the revision has no enabled schedule. */
export function scheduleText(schedule: { enabled?: boolean; cadenceSeconds?: number | null; timeZoneId?: string | null } | null | undefined): string {
  if (!schedule?.enabled) return t('configured.scheduleOff');
  const every = typeof schedule.cadenceSeconds === 'number'
    ? t('configured.scheduleEvery', { interval: formatDuration(schedule.cadenceSeconds * 1000) }) : null;
  return [every, schedule.timeZoneId].filter(Boolean).join(' · ') || t('configured.scheduleOff');
}
