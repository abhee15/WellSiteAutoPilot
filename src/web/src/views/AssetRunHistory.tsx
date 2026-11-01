import { ActionButton } from '../components/ActionButton';
import { useCallback, useState } from 'react';
import type { AssetResponse, ConfiguredLogicResponse } from '../api/generated/v1/types.gen';
import { hashFor } from '../routing';
import { t } from '../localization';
import { formatTimestamp, shortId } from '../format';
import { LoadingIndicator } from '../components/LoadingIndicator';
import { Status } from '../components/Status';
import { ExecutionDetailPanel } from './ExecutionDetailPanel';
import { fetchRunHistory } from './assetActivity';
import { useLiveResource } from './useLiveResource';
import './assetPage.css';

export function AssetRunHistory({ assetId, logicId, runId, assets, configuredLogic }: {
  assetId: string; logicId: string; runId: string | null;
  assets: AssetResponse[]; configuredLogic: ConfiguredLogicResponse[];
}) {
  const [cursor, setCursor] = useState<string | null>(null);
  const [selected, setSelected] = useState(runId);
  const load = useCallback((signal: AbortSignal) => fetchRunHistory(assetId, logicId, cursor, signal), [assetId, logicId, cursor]);
  const history = useLiveResource(`${assetId}:${logicId}:${cursor}`, load, ['executions']);
  const assetName = assets.find(asset => asset.id === assetId)?.name ?? shortId(assetId);
  const logicName = configuredLogic.find(item => item.id === logicId)?.name ?? shortId(logicId);
  return <section className="panel asset-page" aria-label={t('assetDetail.historyTitle')}>
    <header className="asset-page-header"><div><h3>{t('assetDetail.historyTitle')}</h3>
      <p>{assetName} · {logicName}</p><p className="asset-page-meta">{t('assetDetail.historyScope')}</p></div>
      <a href={hashFor('assets', assetId)}>{t('assetDetail.backToAsset')}</a></header>
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
  </section>;
}
