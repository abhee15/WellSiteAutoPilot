import { useEffect, useRef, useState } from 'react';
import { t } from '../localization';
import { loadDeploymentParity } from './deploymentMigrationPreflight';
import type { DeploymentParity } from './deploymentMigrationPreflight';
import type { VerifiedSecurityUser } from './accessManagement';

/** Deployment preview is read-only. Even a matching snapshot does not approve
 * writes, rollout, cutover or an automatic authorization switch. */
export function DeploymentMigrationPreflightPanel({ users }: {
  users: readonly VerifiedSecurityUser[];
}) {
  const [report, setReport] = useState<DeploymentParity | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(false);
  const request = useRef<AbortController | null>(null);
  useEffect(() => () => request.current?.abort(), []);

  const inspect = async () => {
    if (loading) return;
    request.current?.abort();
    const controller = new AbortController();
    request.current = controller;
    setLoading(true); setError(false); setReport(null);
    try {
      const data = await loadDeploymentParity(controller.signal);
      if (!controller.signal.aborted) setReport(data);
    } catch {
      if (!controller.signal.aborted) setError(true);
    } finally {
      if (request.current === controller) {
        request.current = null; setLoading(false);
      }
    }
  };
  const userNames = new Map(users.map(user => [
    user.userId.toLowerCase(), user.displayName || user.identityName,
  ]));
  return (
    <section className="panel" aria-label={t('grants.deploymentHeading')}>
      <div className="panel-header"><h3>{t('grants.deploymentHeading')}</h3></div>
      <p className="access-note">{t('grants.deploymentExplain')}</p>
      <button type="button" className="secondary-button"
        disabled={loading} onClick={() => void inspect()}>
        {loading ? t('access.loading') : t('grants.deploymentInspect')}
      </button>
      {error && <p role="alert" className="execution-detail-error">
        {t('grants.deploymentFailed')}
      </p>}
      {report && <div role="region" aria-label={t('grants.deploymentResults')}>
        <p className="access-warning">{t('grants.deploymentWarning')}</p>
        <p>{t('grants.deploymentCounts', {
          users: report.activeUserCount, assets: report.activeAssetCount,
          matched: report.matchingUserCount,
        })}</p>
        <p>{t('grants.migrationDifferences', {
          expansions: report.expansionCount, reductions: report.reductionCount,
        })}</p>
        <p><strong>{report.allActiveUsersMatchRecordedGrants
          ? t('grants.deploymentMatches') : t('grants.deploymentNotMatching')}</strong></p>
        {report.users.filter(user => !user.recordedGrantsMatchLegacy).length > 0 &&
          <div className="table-wrap">
            <table className="activity-table">
              <caption>{t('grants.deploymentDifferenceList')}</caption>
              <thead><tr>
                <th scope="col">{t('grants.chooseUser')}</th>
                <th scope="col">{t('grants.deploymentExpansion')}</th>
                <th scope="col">{t('grants.deploymentReduction')}</th>
              </tr></thead>
              <tbody>
                {report.users.filter(user => !user.recordedGrantsMatchLegacy)
                  .map(user => <tr key={user.userId}>
                    <th scope="row">{userNames.get(user.userId.toLowerCase())
                      ?? user.userId}</th>
                    <td>{user.expansionCount}</td><td>{user.reductionCount}</td>
                  </tr>)}
              </tbody>
            </table>
          </div>}
      </div>}
    </section>
  );
}
