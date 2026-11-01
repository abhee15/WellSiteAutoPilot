import type { AssetResponse, ConfiguredLogicActivityResponse, ConfiguredLogicResponse } from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { formatTimestamp, shortId } from '../format';
import { hashFor } from '../routing';
import { Status } from '../components/Status';
import { ActionButton } from '../components/ActionButton';
import { LoadingIndicator } from '../components/LoadingIndicator';
import { scheduleText } from './logicSummary';
import { orderedRevisions } from './configuredLogicViews';
import './logicSummary.css';

/** Operational observations do not replace the immutable revision snapshot being reviewed. */
export function ConfiguredLogicRevisionOverview({ logic, assets = [], activity, loading = false, failed = false,
  canReadRuns = false, onHistory, onRetry }: {
  logic: ConfiguredLogicResponse; assets?: readonly AssetResponse[]; activity?: ConfiguredLogicActivityResponse;
  loading?: boolean; failed?: boolean; canReadRuns?: boolean; onHistory?: () => void; onRetry?: () => void;
}) {
  const revisions = orderedRevisions(logic);
  const active = revisions.find(revision => revision.id === logic.activeRevisionId);
  const shown = active ?? revisions[0];
  if (!shown) return <p>{t('configured.noRevisions')}</p>;
  const targets = [...new Set(shown.assetBindings?.map(binding => binding.assetId) ?? [])];
  const providers = [...new Set(shown.dataBindings?.map(binding => binding.providerId) ?? [])];
  const name = (id?: string) => assets.find(asset => asset.id === id)?.name ?? shortId(id);
  let purpose: string | null = null;
  try {
    const description: unknown = (JSON.parse(shown.moduleManifestJson ?? '{}') as { description?: unknown }).description;
    if (typeof description === 'string' && description.trim()) purpose = description;
  } catch { /* A missing description does not imply a purpose or a readiness verdict. */ }
  const matching = activity && activity.activeRevisionId === logic.activeRevisionId;
  return <section className="logic-detail-overview" role="region" aria-label={t('logicDetail.overview')}>
    {purpose && <><h4>{t('logicDetail.purpose')}</h4><p>{purpose}</p></>}
    <dl className="logic-facts">
      <div><dt>{t(activity && !matching ? 'logicDetail.reviewedRevision' : 'configured.liveRevision')}</dt><dd>{active ? t('setup.revision', { number: active.revisionNumber ?? '—' }) : t('configured.noLive')}</dd></div>
      <div><dt>{t('configured.module')}</dt><dd>{shown.moduleId} v{shown.moduleVersion}</dd></div>
      <div><dt>{t('logicDetail.mode')}</dt><dd>{shown.mode}</dd></div>
      <div><dt>{t('logicDetail.targetLabel')}</dt><dd>{targets.slice(0, 2).map(id => name(id)).join(', ') || '—'}
        {targets.length > 2 && <> · {t('logicDetail.moreTargets', { count: targets.length - 2 })}</>}</dd></div>
      <div><dt>{t('logicDetail.providerLabel')}</dt><dd>{providers.join(', ') || t('logicDetail.noProviders')}</dd></div>
      <div><dt>{t('configured.schedule')}</dt><dd>{scheduleText(active?.schedule)}</dd></div>
    </dl>
    {active?.schedule?.enabled && <p>{t('logicDetail.nextScheduled')}: {matching && activity.nextScheduledAtUtc
      ? formatTimestamp(activity.nextScheduledAtUtc) : t('logicDetail.notAvailable')}</p>}
    <p className="logic-detail-note">{t('assetDetail.scheduleHint')}</p>
    {shown.view && <p className="logic-detail-note">{t('assetDetail.viewSchedule')}</p>}
    {loading && <LoadingIndicator label={t('assetPage.loading')} />}
    {failed && <p role="status">{t('logicDetail.activityUnavailable')} <ActionButton variant="quiet" label={t('assetDetail.retry')} onClick={onRetry} /></p>}
    {canReadRuns && <section className="logic-detail-section">
      <div className="logic-detail-row"><h4>{t('assetDetail.latestRuns')}</h4><ActionButton variant="quiet" label={t('assetDetail.history')} onClick={onHistory} /></div>
      {activity?.recentRuns?.length === 0 && <p>{t('logicSummary.noRuns')}</p>}
      <ul className="asset-page-list">{activity?.recentRuns?.map(run => <li key={run.executionId}>
        <Status value={run.status} /><a href={hashFor('operations', run.executionId)}>{formatTimestamp(run.requestedAtUtc)}</a>
      </li>)}</ul>
    </section>}
    {activity?.observedAtUtc && <p className="logic-detail-note">{t('logicDetail.observed', { time: formatTimestamp(activity.observedAtUtc) })}</p>}
  </section>;
}
