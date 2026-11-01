import { useCallback } from 'react';
import type { AssetLogicSummaryResponse } from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { formatTimestamp, shortId } from '../format';
import { statusLabel } from '../words';
import { hashFor } from '../routing';
import { Status } from '../components/Status';
import { LoadingIndicator } from '../components/LoadingIndicator';
import { fetchLogicRuns, runHistoryHash } from './assetActivity';
import { useLiveResource } from './useLiveResource';

export function AssetLogicCard({ item, assetId, canReadRuns }: {
  item: AssetLogicSummaryResponse; assetId: string; canReadRuns: boolean;
}) {
  const configuration = item.configuration!;
  const id = configuration.id!;
  const active = configuration.revisions?.find(revision => revision.id === configuration.activeRevisionId);
  const revision = active ?? [...(configuration.revisions ?? [])].sort((a, b) => (b.revisionNumber ?? 0) - (a.revisionNumber ?? 0))[0];
  const currentlyBound = active?.assetBindings?.some(binding => binding.assetId === assetId);
  const scheduleLabel = !active ? t('assetDetail.notActive') : !currentlyBound ? t('assetDetail.previousBinding')
    : !active.schedule ? t('assetDetail.manual') : !active.schedule.enabled ? t('assetDetail.paused')
    : item.nextScheduledAtUtc ? t('assetDetail.next', { time: formatTimestamp(item.nextScheduledAtUtc) })
    : t('assetDetail.scheduleUnavailable');
  return <article className="asset-logic-card" aria-label={configuration.name ?? id}>
    <header>
      <a className="asset-logic-name" href={hashFor('logic', id)}>{configuration.name}</a>
      <Status value={active ? 'Active' : revision?.status ?? 'Draft'} />
    </header>
    {revision && <p className="asset-page-meta">{revision.moduleId} · v{revision.moduleVersion} · {revision.mode}</p>}
    <p className="asset-schedule">{scheduleLabel}</p>
    {revision?.view && <p className="asset-page-meta">{t('assetDetail.viewSchedule')}</p>}
    {canReadRuns && <AssetRunPreview assetId={assetId} logicId={id} logicName={configuration.name ?? id} />}
  </article>;
}

/** Also keeps run-only readers informed without requiring configuration-read permission. */
export function AssetRunPreview({ assetId, logicId, logicName }: { assetId: string; logicId?: string; logicName?: string }) {
  const load = useCallback((signal: AbortSignal) => fetchLogicRuns(assetId, logicId, signal), [assetId, logicId]);
  const runs = useLiveResource(`${assetId}:${logicId ?? ''}`, load, ['executions']);
  const Heading = logicId ? 'h5' : 'h4';
  return <div className="asset-logic-runs">
    <div className="asset-logic-runs-heading"><Heading>{t('assetDetail.latestRuns')}</Heading>
      {logicId && <a href={runHistoryHash(assetId, logicId)} aria-label={t('assetDetail.historyFor', { name: logicName ?? logicId })}>{t('assetDetail.history')}</a>}</div>
    {runs.loading && <LoadingIndicator label={t('assetPage.loading')} />}
    {runs.failed && <p role="status" className="asset-page-empty">{t('assetDetail.runsUnavailable')} <button type="button" className="row-link" onClick={runs.refresh}>{t('assetDetail.retry')}</button></p>}
    {runs.data?.length === 0 && <p className="asset-page-empty">{t('assetPage.noRuns')}</p>}
    {runs.data && runs.data.length > 0 && <ul className="asset-page-list">
      {runs.data.map(run => <li key={run.executionId}>
        <Status value={run.status} />
        <a aria-label={t('assetDetail.runAt', { time: formatTimestamp(run.requestedAtUtc), status: statusLabel(run.status), id: shortId(run.executionId) })} href={logicId || run.logicInstanceId ? runHistoryHash(assetId, logicId ?? run.logicInstanceId!, run.executionId) : hashFor('operations', run.executionId)}>{formatTimestamp(run.requestedAtUtc)}</a>
      </li>)}
    </ul>}
  </div>;
}
