import type { AssetResponse, ConfiguredLogicResponse } from '../api/generated/v1/types.gen';
import { hashFor } from '../routing';
import { t } from '../localization';
import { shortId } from '../format';
import { ExecutionHistory } from './ExecutionHistory';
import './assetPage.css';

export function AssetRunHistory({ assetId, logicId, runId, assets, configuredLogic }: {
  assetId: string; logicId: string; runId: string | null;
  assets: AssetResponse[]; configuredLogic: ConfiguredLogicResponse[];
}) {
  const assetName = assets.find(asset => asset.id === assetId)?.name ?? shortId(assetId);
  const logicName = configuredLogic.find(item => item.id === logicId)?.name ?? shortId(logicId);
  return <section className="panel asset-page" aria-label={t('assetDetail.historyTitle')}>
    <header className="asset-page-header"><div><h3>{t('assetDetail.historyTitle')}</h3>
      <p>{assetName} · {logicName}</p><p className="asset-page-meta">{t('assetDetail.historyScope')}</p></div>
      <a href={hashFor('assets', assetId)}>{t('assetDetail.backToAsset')}</a></header>
    <ExecutionHistory assetId={assetId} logicId={logicId} runId={runId} assets={assets} configuredLogic={configuredLogic} />
  </section>;
}
