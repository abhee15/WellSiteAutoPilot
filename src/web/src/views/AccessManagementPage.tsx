import { useCallback, useEffect, useRef, useState } from 'react';
import { loadSecurityUsers } from './accessManagement';
import type { VerifiedSecurityUser } from './accessManagement';
import { t } from '../localization';
import { formatDetailTimestamp } from './executionDetail';
import './accessManagement.css';
import { LoadingIndicator } from '../components/LoadingIndicator';

interface AccessManagementPageProps {
  currentUserId: string;
  /** Opens role assignment for the chosen user. */
  onAssignRole?: (userId: string) => void;
}

export function AccessManagementPage({ currentUserId, onAssignRole }: AccessManagementPageProps) {
  const [users, setUsers] = useState<VerifiedSecurityUser[]>([]);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [awaitingOnly, setAwaitingOnly] = useState(false);
  const request = useRef<AbortController | null>(null);
  const reload = useCallback(async () => {
    request.current?.abort();
    const controller = new AbortController(); request.current = controller;
    setUsers([]); setLoading(true); setFailed(false);
    try {
      const rows = await loadSecurityUsers(controller.signal, fetch, { awaitingAccessOnly: awaitingOnly });
      if (!controller.signal.aborted) setUsers(rows);
    } catch {
      if (!controller.signal.aborted) setFailed(true);
    } finally {
      if (!controller.signal.aborted) setLoading(false);
    }
  }, [awaitingOnly]);
  useEffect(() => { void reload(); return () => request.current?.abort(); }, [reload, currentUserId]);
  const canAssign = (user: VerifiedSecurityUser) =>
    Boolean(onAssignRole) && user.isActive && user.userId.toLowerCase() !== currentUserId.toLowerCase();
  return <section className="panel">
    <div className="panel-header"><h2>{t('access.users')}</h2>
      <button type="button" className="secondary-button" disabled={loading} onClick={() => void reload()}>{t('access.refresh')}</button></div>
    <p className="access-intro">{t('access.identityDirectoryNote')}</p>
    <p className="access-intro">{t('access.groupNote')}</p>
    <label className="access-filter">
      <input type="checkbox" checked={awaitingOnly} disabled={loading}
        onChange={event => setAwaitingOnly(event.target.checked)} />
      {t('access.awaitingOnly')}
    </label>
    {loading && <LoadingIndicator label={t('access.loading')} />}
    {failed && <p role="alert">{t('access.denied')}</p>}
    {!loading && !failed && awaitingOnly && users.length === 0 &&
      <p role="status">{t('access.noWaiting')}</p>}
    {!loading && !failed && users.length > 0 && <div className="table-wrap"><table className="activity-table">
      <thead><tr><th>{t('access.identity')}</th><th>{t('access.directRoles')}</th><th>{t('session.account')}</th>
        <th>{t('access.lastSeen')}</th><th>{t('access.actions')}</th></tr></thead>
      <tbody>{users.map(user => <tr key={user.userId}>
        <td><strong>{user.displayName || user.identityName}</strong>{user.displayName && <small>{user.identityName}</small>}</td>
        <td>{user.activeDirectGrantCount > 0 ? user.activeDirectGrantCount : t('access.noDirectRole')}</td>
        <td>{user.isActive ? t('session.active') : t('session.inactive')}</td>
        <td>{formatDetailTimestamp(user.lastSeenAtUtc)}</td>
        <td>{canAssign(user) && <button type="button" className="secondary-button"
          aria-label={t('access.assignRoleTo', { name: user.displayName || user.identityName })}
          onClick={() => onAssignRole?.(user.userId)}>{t('access.assignRole')}</button>}</td>
      </tr>)}</tbody></table></div>}
  </section>;
}
