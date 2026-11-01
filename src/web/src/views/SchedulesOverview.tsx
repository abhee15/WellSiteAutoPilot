import { useEffect, useMemo, useState } from 'react';
import { hashFor } from '../routing';
import type { AssetResponse, ConfiguredLogicResponse, ExecutionResponse } from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { EmptyRow, PanelHeader } from '../components/common';
import { Status } from '../components/Status';
import { formatTimestamp } from '../format';
import { statusLabel } from '../words';
import { scheduleText } from './logicSummary';
import { summarizeAssetLabels } from './runContext';
import { buildScheduleOverview } from './scheduleOverview';

const CLOCK_REFRESH_MS = 15_000;

type Props = {
  configuredLogic: readonly ConfiguredLogicResponse[];
  assets: readonly AssetResponse[];
  /** Present only for people who may read runs; without it the last-run column is not shown. */
  executions?: readonly ExecutionResponse[];
  isLoaded: boolean;
  nowMs?: number;
};

/**
 * What is scheduled to run, how often, and when it runs next. Read-only: a schedule belongs to a configuration
 * revision, so changing one means saving a new revision from the configuration itself, which each row links to.
 */
export function SchedulesOverview({ configuredLogic, assets, executions, isLoaded, nowMs }: Props) {
  // The clock moves on even when no data changes, so a run that has just happened is not shown as still coming.
  const [tick, setTick] = useState(() => Date.now());
  useEffect(() => {
    if (nowMs !== undefined) return;
    const timer = setInterval(() => setTick(Date.now()), CLOCK_REFRESH_MS);
    return () => clearInterval(timer);
  }, [nowMs]);
  const clock = nowMs ?? tick;
  const overview = useMemo(
    () => buildScheduleOverview(configuredLogic, assets, executions ?? [], clock),
    [configuredLogic, assets, executions, clock],
  );
  const showRuns = executions !== undefined;
  return (
    <div className="split-panes">
      <section className="panel split-browse" aria-label={t('logic.tabSchedules')}>
        <PanelHeader title={t('schedules.heading')} count={overview.rows.length} />
        <p className="split-note">{t('schedules.intro')}</p>
        <div className="table-wrap">
          <table className="activity-table">
            <thead>
              <tr>
                <th>{t('schedules.logic')}</th>
                <th>{t('schedules.frequency')}</th>
                <th>{t('schedules.nextRun')}</th>
                <th className="run-col-secondary">{t('schedules.assets')}</th>
                <th className="run-col-secondary">{t('schedules.mode')}</th>
                {showRuns && <th className="run-col-secondary">{t('schedules.lastRun')}</th>}
              </tr>
            </thead>
            <tbody>
              {overview.rows.map(row => (
                <tr key={row.id}>
                  <td><a href={hashFor('logic', row.id)}>{row.name}</a></td>
                  <td>{row.enabled
                    ? scheduleText({ enabled: true, cadenceSeconds: row.cadenceSeconds, timeZoneId: row.timeZoneId })
                    : <Status value={t('schedules.off')} />}</td>
                  <td>{row.enabled && row.nextRunAtUtc ? formatTimestamp(row.nextRunAtUtc) : '—'}</td>
                  <td className="run-col-secondary">{summarizeAssetLabels(row.assetLabels) || '—'}</td>
                  <td className="run-col-secondary">{row.mode ?? '—'}</td>
                  {showRuns && <td className="run-col-secondary">{row.lastRun
                    ? `${formatTimestamp(row.lastRun.requestedAtUtc)} · ${statusLabel(row.lastRun.status)}`
                    : t('configured.noRecentRun')}</td>}
                </tr>
              ))}
              {overview.rows.length === 0 && (
                <EmptyRow columns={showRuns ? 6 : 5} loading={!isLoaded}>
                  {!isLoaded ? t('views.loading') : t('schedules.none')}
                </EmptyRow>
              )}
            </tbody>
          </table>
        </div>
        {isLoaded && overview.unscheduledCount > 0 && (
          <p className="split-note">{t('schedules.unscheduled', { count: overview.unscheduledCount })}</p>
        )}
      </section>
    </div>
  );
}
