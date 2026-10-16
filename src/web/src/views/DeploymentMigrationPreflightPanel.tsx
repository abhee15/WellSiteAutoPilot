import { useEffect, useRef, useState } from 'react';
import { t } from '../localization';
import { loadDeploymentParity } from './deploymentMigrationPreflight';
import type { DeploymentParity } from './deploymentMigrationPreflight';
import { materializeLegacyGrants } from './materializeLegacyGrants';
import type { GrantMigrationReceipt } from './materializeLegacyGrants';
import { rollbackLegacyGrants } from './rollbackLegacyGrants';
import type { GrantRollbackReceipt } from './rollbackLegacyGrants';
import type { VerifiedSecurityUser } from './accessManagement';

/** Explicit audit-only writes. A diagnostic snapshot never enables live
 * scoped authorization; the server revalidates the entire population. */
export function DeploymentMigrationPreflightPanel({ users, onWritingChange }: {
  users: readonly VerifiedSecurityUser[];
  onWritingChange?: (writing: boolean) => void;
}) {
  const [report, setReport] = useState<DeploymentParity | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(false);
  const [confirmed, setConfirmed] = useState(false);
  const [confirmationText, setConfirmationText] = useState('');
  const [writing, setWriting] = useState(false);
  const [writeFailed, setWriteFailed] = useState(false);
  const [receipt, setReceipt] = useState<GrantMigrationReceipt | null>(null);
  const [rollbackConfirmed, setRollbackConfirmed] = useState(false);
  const [rollbackText, setRollbackText] = useState('');
  const [rollbackFailed, setRollbackFailed] = useState(false);
  const [rollbackReceipt, setRollbackReceipt] = useState<GrantRollbackReceipt | null>(null);
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
    setConfirmed(false); setConfirmationText(''); setReceipt(null); setWriteFailed(false);
    setRollbackConfirmed(false); setRollbackText('');
    setRollbackReceipt(null); setRollbackFailed(false);
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
    setWriting(true); onWritingChange?.(true);
    setWriteFailed(false); setRollbackFailed(false); setRollbackReceipt(null); setReceipt(null);
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
        writeRequest.current = null; setWriting(false); onWritingChange?.(false);
      }
    }
  };
  const rollback = async () => {
    if (!report || loading || writing || writeRequest.current ||
      !report.users.some(user => user.recordedGrantCount > 0) ||
      !rollbackConfirmed ||
      rollbackText !== t('grants.rollbackPhrase')) return;
    const abort = new AbortController();
    writeRequest.current = abort;
    setWriting(true); onWritingChange?.(true);
    setWriteFailed(false); setReceipt(null); setRollbackFailed(false); setRollbackReceipt(null);
    try {
      const saved = await rollbackLegacyGrants(true, abort.signal);
      if (!abort.signal.aborted) {
        setRollbackReceipt(saved); setReport(null);
        setRollbackConfirmed(false); setRollbackText('');
      }
    } catch {
      if (!abort.signal.aborted) {
        // The server could have committed before the connection was lost.
        // Always re-inspect instead of retrying a potentially completed write.
        setReport(null); setRollbackFailed(true);
        setRollbackConfirmed(false); setRollbackText('');
      }
    } finally {
      if (writeRequest.current === abort) {
        writeRequest.current = null; setWriting(false); onWritingChange?.(false);
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
      {rollbackReceipt && <p role="status">{t('grants.rollbackSaved', {
        count: rollbackReceipt.grantsDeactivated,
      })}</p>}
      {rollbackFailed && <p role="alert" className="execution-detail-error">
        {t('grants.rollbackUncertain')}
      </p>}
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
        {report.users.some(user => user.recordedGrantCount > 0) &&
          <div className="access-review">
            <strong>{t('grants.rollbackHeading')}</strong>
            <p className="access-warning">{t('grants.rollbackWarning')}</p>
            <label className="access-confirm">
              <input type="checkbox" checked={rollbackConfirmed} disabled={writing}
                onChange={event => setRollbackConfirmed(event.target.checked)} />
              {t('grants.rollbackConfirm')}
            </label>
            <label>{t('grants.rollbackType')}
              <input value={rollbackText} maxLength={80} disabled={writing}
                onChange={event => setRollbackText(event.target.value)}
                autoComplete="off" />
            </label>
            <button type="button" className="secondary-button"
              disabled={writing || !rollbackConfirmed ||
                rollbackText !== t('grants.rollbackPhrase')}
              onClick={() => void rollback()}>
              {writing ? t('access.saving') : t('grants.rollbackAction')}
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
