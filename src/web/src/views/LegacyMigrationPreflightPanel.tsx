import { useEffect, useRef, useState } from 'react';
import { t } from '../localization';
import type { AssetChoice } from './scopedAccessGrants';
import { loadLegacyMigrationPreflight } from './legacyMigrationPreflight';
import type { LegacyMigrationPreflight } from './legacyMigrationPreflight';

/** Snapshot parity is advisory: this view intentionally has no migrate,
 * approve, enable or cutover action. */
export function LegacyMigrationPreflightPanel({ userId, assets }: {
  userId: string; assets: readonly AssetChoice[];
}) {
  const [report, setReport] = useState<LegacyMigrationPreflight | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(false);
  const controller = useRef<AbortController | null>(null);
  useEffect(() => () => controller.current?.abort(), []);

  const inspect = async () => {
    if (loading) return;
    controller.current?.abort();
    const abort = new AbortController();
    controller.current = abort;
    setLoading(true); setError(false); setReport(null);
    try {
      const result = await loadLegacyMigrationPreflight(userId, abort.signal);
      if (!abort.signal.aborted) setReport(result);
    } catch {
      if (!abort.signal.aborted) setError(true);
    } finally {
      if (controller.current === abort) {
        controller.current = null; setLoading(false);
      }
    }
  };

  const names = new Map(assets.map(item => [item.id.toLowerCase(), item.name]));
  return (
    <section className="panel" aria-label={t('grants.migrationHeading')}>
      <div className="panel-header"><h3>{t('grants.migrationHeading')}</h3></div>
      <p className="access-note">{t('grants.migrationExplanation')}</p>
      <button type="button" className="secondary-button"
        disabled={loading} onClick={() => void inspect()}>
        {loading ? t('access.loading') : t('grants.migrationInspect')}
      </button>
      {error && <p role="alert" className="execution-detail-error">
        {t('grants.migrationFailed')}
      </p>}
      {report && <div role="region" aria-label={t('grants.migrationResults')}>
        <p className="access-warning">{t('grants.migrationSnapshotWarning')}</p>
        <p>{t('grants.migrationInventory', {
          assets: report.activeAssetCount,
          expected: report.expectedLegacyGrantCount,
          recorded: report.recordedGrantCount,
        })}</p>
        <p>{t('grants.migrationDifferences', {
          expansions: report.expansionCount,
          reductions: report.reductionCount,
        })}</p>
        <p><strong>{report.recordedGrantsMatchLegacy
          ? t('grants.migrationMatches') : t('grants.migrationNotMatching')}</strong></p>
        {report.differenceSamples.length > 0 &&
          <div className="table-wrap">
            <table className="activity-table">
              <caption>{t('grants.migrationSamples')}</caption>
              <thead><tr>
                <th scope="col">{t('grants.inspectorAsset')}</th>
                <th scope="col">{t('grants.inspectorPermission')}</th>
                <th scope="col">{t('grants.inspectorLive')}</th>
                <th scope="col">{t('grants.inspectorProposed')}</th>
              </tr></thead>
              <tbody>
                {report.differenceSamples.map(item => <tr
                  key={(item.assetId ?? 'global') + ':' + item.permission}>
                  <th scope="row">{item.assetId
                    ? names.get(item.assetId.toLowerCase()) ?? item.assetId
                    : t('grants.inspectorGlobal')}</th>
                  <td>{item.permission}</td>
                  <td>{item.legacyAllowed ? t('grants.allowed') : t('grants.denied')}</td>
                  <td>{item.recordedGrantAllowed
                    ? t('grants.allowed') : t('grants.denied')}</td>
                </tr>)}
              </tbody>
            </table>
          </div>}
      </div>}
    </section>
  );
}
