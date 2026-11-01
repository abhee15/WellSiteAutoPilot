import { ActionButton } from '../components/ActionButton';
import { useCallback, useState } from 'react';
import type { AssetResponse, ConfiguredLogicResponse } from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { formatTimestamp, shortId } from '../format';
import { LoadingIndicator } from '../components/LoadingIndicator';
import { Status } from '../components/Status';
import { ExecutionDetailPanel } from './ExecutionDetailPanel';
import { fetchRunHistory } from './executionHistoryApi';
import { useLiveResource } from './useLiveResource';
import './assetPage.css';

/** One scoped paging/inspection flow, shared by Asset history and configured-logic Activity. */
export function ExecutionHistory({ assetId, logicId, runId = null, assets, configuredLogic, enabled = true }: {
  assetId?: string; logicId: string; runId?: string | null;
  assets: readonly AssetResponse[]; configuredLogic: readonly ConfiguredLogicResponse[]; enabled?: boolean;
}) {
  const [cursor, setCursor] = useState<string | null>(null);
  const [selected, setSelected] = useState(runId);
  const load = useCallback((signal: AbortSignal) => fetchRunHistory(assetId, logicId, cursor, signal), [assetId, logicId, cursor]);
  const history = useLiveResource(`${assetId}:${logicId}:${cursor}`, load, ['executions'], enabled);
  return <div className="execution-history">
    {history.loading && <LoadingIndicator label={t('assetPage.loading')} />}
    {history.failed && <p role="status">{t('assetDetail.runsUnavailable')} <button type="button" className="row-link" onClick={history.refresh}>{t('assetDetail.retry')}</button></p>}
    {history.data?.items?.length === 0 && <p>{t('assetPage.noRuns')}</p>}
    <ul className="asset-page-list">{history.data?.items?.map(run => <li key={run.executionId}>
      <Status value={run.status} /><button type="button" className="row-link" aria-pressed={selected === run.executionId}
        aria-label={`${formatTimestamp(run.requestedAtUtc)} · ${t('assetDetail.inspect', { id: shortId(run.executionId) })}`}
        onClick={() => setSelected(run.executionId ?? null)}>{formatTimestamp(run.requestedAtUtc)}</button>
      <small>{run.mode} · {shortId(run.executionId)}</small>
    </li>)}</ul>
    <div className="asset-detail-actions">
      {cursor && <ActionButton onClick={() => setCursor(null)} label={t('assetDetail.newest')} />}
      {history.data?.nextCursor && <ActionButton onClick={() => setCursor(history.data!.nextCursor!)} label={t('assetDetail.older')} />}
    </div>
    {selected && <ExecutionDetailPanel key={selected} executionId={selected} assets={assets} configuredLogic={configuredLogic}
      poll onClose={() => setSelected(null)} onAccessLost={() => setSelected(null)} />}
  </div>;
}
