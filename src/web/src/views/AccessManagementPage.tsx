import { useCallback, useEffect, useRef, useState } from 'react';
import { loadSecurityUsers } from './accessManagement';
import type { VerifiedSecurityUser } from './accessManagement';
import { t } from '../localization';
import { formatDetailTimestamp } from './executionDetail';

export function AccessManagementPage({ currentUserId }: { currentUserId: string }) {
  const [users, setUsers] = useState<VerifiedSecurityUser[]>([]);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const request = useRef<AbortController | null>(null);
  const reload = useCallback(async () => {
    request.current?.abort();
    const controller = new AbortController(); request.current = controller;
    setUsers([]); setLoading(true); setFailed(false);
    try {
      const rows = await loadSecurityUsers(controller.signal);
      if (!controller.signal.aborted) setUsers(rows);
    } catch {
      if (!controller.signal.aborted) setFailed(true);
    } finally {
      if (!controller.signal.aborted) setLoading(false);
    }
  }, []);
  useEffect(() => { void reload(); return () => request.current?.abort(); }, [reload, currentUserId]);
  return <section className="panel">
    <div className="panel-header"><h2>{t('access.users')}</h2>
      <button type="button" className="secondary-button" disabled={loading} onClick={() => void reload()}>{t('access.refresh')}</button></div>
    <p className="access-intro">{t('access.identityDirectoryNote')}</p>
    {loading && <p role="status">{t('access.loading')}</p>}
    {failed && <p role="alert">{t('access.denied')}</p>}
    {!loading && !failed && <div className="table-wrap"><table className="activity-table">
      <thead><tr><th>{t('access.identity')}</th><th>{t('session.account')}</th><th>{t('access.lastSeen')}</th></tr></thead>
      <tbody>{users.map(user => <tr key={user.userId}>
        <td><strong>{user.displayName || user.identityName}</strong><small>{user.identityName}</small></td>
        <td>{user.isActive ? t('session.active') : t('session.inactive')}</td>
        <td>{formatDetailTimestamp(user.lastSeenAtUtc)}</td>
      </tr>)}</tbody></table></div>}
  </section>;
}
