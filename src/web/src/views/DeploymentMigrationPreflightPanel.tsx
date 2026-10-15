import { useEffect, useRef, useState } from 'react';
import { t } from '../localization';
import { loadDeploymentParity } from './deploymentMigrationPreflight';
import type { DeploymentParity } from './deploymentMigrationPreflight';
import { materializeLegacyGrants } from './materializeLegacyGrants';
import type { GrantMigrationReceipt } from './materializeLegacyGrants';
import type { VerifiedSecurityUser } from './accessManagement';

/** Deployment preview is read-only. Even a matching snapshot does not approve
 * writes, rollout, cutover or an automatic authorization switch. */
export function DeploymentMigrationPreflightPanel({ users }: {
  users: readonly VerifiedSecurityUser[];
}) {
  const [report, setReport] = useState<DeploymentParity | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(false);
  const [confirmed, setConfirmed] = useState(false);
  const [confirmationText, setConfirmationText] = useState('');
  const [writing, setWriting] = useState(false);
  const [writeFailed, setWriteFailed] = useState(false);
  const [receipt, setReceipt] = useState<GrantMigrationReceipt | null>(null);
  const writeRequest = useRef<AbortController | null>(null);
  const request = useRef<AbortController | null>(null);
  useEffect(() => () => {
    request.current?.abort();
    writeRequest.current?.abort();
  }, []);

  const inspect = async () => {
    if (loading || writing) return;
    request.current?.abort();
    const controller = new AbortController();
    request.current = controller;
    setLoading(true); setError(false); setReport(null);
    setConfirmed(false); setConfirmationText(''); setReceipt(null);
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
  const save = async () => {
    if (!report || writing || loading || writeRequest.current ||
      report.allActiveUsersMatchRecordedGrants || report.expansionCount !== 0 ||
      report.reductionCount === 0 || !confirmed ||
      confirmationText !== t('grants.materializePhrase')) return;
    const abort = new AbortController();
    writeRequest.current = abort;
    setWriting(true); setWriteFailed(false); setReceipt(null);
    try {
      const saved = await materializeLegacyGrants(true, abort.signal);
      if (!abort.signal.aborted) {
        setReceipt(saved); setReport(null);
        setConfirmed(false); setConfirmationText('');
      }
    } catch {
      if (!abort.signal.aborted) {
        // An uncertain response may follow a committed transaction. Do not
        // automatically send another POST. Force a fresh preflight instead.
        setReport(null); setWriteFailed(true);
        setConfirmed(false); setConfirmationText('');
      }
    } finally {
      if (writeRequest.current === abort) {
        writeRequest.current = null; setWriting(false);
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
        disabled={loading || writing} onClick={() => void inspect()}>
        {loading ? t('access.loading') : t('grants.deploymentInspect')}
      </button>
      {receipt && <p role="status">{t('grants.materializeSaved', {
        count: receipt.grantsCreated,
      })}</p>}
      {writeFailed && <p role="alert" className="execution-detail-error">
        {t('grants.materializeUncertain')}
      </p>}
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
        {!report.allActiveUsersMatchRecordedGrants &&
          report.expansionCount === 0 && report.reductionCount > 0 &&
          <div className="access-review">
            <strong>{t('grants.materializeHeading')}</strong>
            <p className="access-warning">{t('grants.materializeWarning')}</p>
            <label className="access-confirm">
              <input type="checkbox" checked={confirmed} disabled={writing}
                onChange={event => setConfirmed(event.target.checked)} />
              {t('grants.materializeConfirm')}
            </label>
            <label>{t('grants.materializeType')}
              <input value={confirmationText} maxLength={80} disabled={writing}
                onChange={event => setConfirmationText(event.target.value)}
                autoComplete="off" />
            </label>
            <button type="button" className="secondary-button"
              disabled={writing || !confirmed ||
                confirmationText !== t('grants.materializePhrase')}
              onClick={() => void save()}>
              {writing ? t('access.saving') : t('grants.materializeAction')}
            </button>
          </div>}
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
