import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { t } from '../localization';
import { loadSecurityUsers } from './accessManagement';
import type { VerifiedSecurityUser } from './accessManagement';
import { loadRoleCatalog } from './roleCatalog';
import type { PermissionDefinition, RoleRecord } from './roleCatalog';
import { createGrant, GrantApiError, loadGrants } from './grantAdministration';
import type { PersistedGrant } from './grantAdministration';
import { previewGrant } from './scopedAccessGrants';
import type { AssetChoice, GrantDraft } from './scopedAccessGrants';
import { ScopedGrantEditor } from './ScopedGrantEditor';

export function GrantAdministrationPage({ currentUserId }: { currentUserId: string }) {
  const [users, setUsers] = useState<VerifiedSecurityUser[]>([]);
  const [roles, setRoles] = useState<RoleRecord[]>([]);
  const [permissions, setPermissions] = useState<PermissionDefinition[]>([]);
  const [assets, setAssets] = useState<AssetChoice[]>([]);
  const [selectedUserId, setSelectedUserId] = useState('');
  const [grants, setGrants] = useState<PersistedGrant[]>([]);
  const [pending, setPending] = useState<GrantDraft | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const requestRef = useRef<AbortController | null>(null);
  const mutationRef = useRef<AbortController | null>(null);

  const selectedUser = users.find(x => x.userId === selectedUserId);
  const selectedRole = roles.find(x => x.roleId === pending?.roleId) ?? null;
  const preview = useMemo(() => {
    if (!pending) return null;
    try { return previewGrant(pending, selectedRole, permissions, assets, true); }
    catch { return null; }
  }, [pending, selectedRole, permissions, assets]);

  const load = useCallback(async () => {
    requestRef.current?.abort();
    const abort = new AbortController();
    requestRef.current = abort;
    setIsLoading(true);
    setError(null);
    setUsers([]); setRoles([]); setPermissions([]); setAssets([]);
    setSelectedUserId(''); setGrants([]); setPending(null);
    try {
      const [directory, catalog, response] = await Promise.all([
        loadSecurityUsers(abort.signal),
        loadRoleCatalog(abort.signal),
        fetch('/api/v1/assets?limit=100', {
          credentials: 'same-origin', cache: 'no-store', signal: abort.signal,
        }),
      ]);
      if (abort.signal.aborted) return;
      if (!response.ok) throw new Error('Assets unavailable');
      const raw: unknown = await response.json();
      if (!Array.isArray(raw) || raw.length > 100 || !raw.every((item: unknown) => {
        if (!item || typeof item !== 'object') return false;
        const row = item as Record<string, unknown>;
        return typeof row.id === 'string' && typeof row.name === 'string' &&
          typeof row.isActive === 'boolean';
      })) throw new Error('Invalid Asset directory');
      setUsers(directory); setRoles(catalog.roles);
      setPermissions(catalog.permissions);
      setAssets(raw as AssetChoice[]);
    } catch {
      if (!abort.signal.aborted) {
        setError(t('grants.loadingFailed'));
      }
    } finally {
      if (requestRef.current === abort) {
        requestRef.current = null; setIsLoading(false);
      }
    }
  }, []);

  useEffect(() => {
    void load();
    return () => {
      requestRef.current?.abort();
      mutationRef.current?.abort();
    };
  }, [load]);

  const pickUser = async (userId: string) => {
    requestRef.current?.abort();
    const abort = new AbortController();
    requestRef.current = abort;
    setSelectedUserId(userId); setPending(null);
    setGrants([]); setError(null); setNotice(null);
    if (!userId) return;
    try {
      const data = await loadGrants(userId, abort.signal);
      if (!abort.signal.aborted) setGrants(data);
    } catch {
      if (!abort.signal.aborted) setError(t('grants.loadingFailed'));
    } finally {
      if (requestRef.current === abort) requestRef.current = null;
    }
  };

  const save = async () => {
    if (!pending || !preview || saving || mutationRef.current) return;
    const abort = new AbortController();
    mutationRef.current = abort;
    setSaving(true); setNotice(null);
    try {
      const persisted = await createGrant(pending, true, abort.signal);
      if (abort.signal.aborted) return;
      const records = await loadGrants(pending.userId, abort.signal);
      if (abort.signal.aborted) return;
      if (!records.some(item => item.grantId === persisted.grantId)) {
        throw new GrantApiError('invalidResponse');
      }
      setGrants(records); setPending(null); setNotice(t('grants.savedDraft'));
    } catch {
      if (!abort.signal.aborted) {
        // An uncertain write must be reconciled with the server, not retried.
        setPending(null); setGrants([]);
        setError(t('grants.saveUncertain'));
      }
    } finally {
      if (mutationRef.current === abort) {
        mutationRef.current = null; setSaving(false);
      }
    }
  };

  return (
    <>
      <section className="panel">
        <div className="panel-header">
          <h2>{t('grants.manage')}</h2>
          <button type="button" className="secondary-button"
            disabled={isLoading || saving} onClick={() => void load()}>
            {t('access.refresh')}
          </button>
        </div>
        <p className="access-note access-intro">{t('grants.draftOnly')}</p>
        {isLoading && <p role="status">{t('access.loading')}</p>}
        {error && <p role="alert" className="execution-detail-error access-intro">{error}</p>}
        {notice && <p role="status" className="access-intro">{notice}</p>}
        {!isLoading && !error && (
          <label className="grant-user-picker">
            {t('grants.chooseUser')}
            <select value={selectedUserId} disabled={saving}
              onChange={event => void pickUser(event.target.value)}>
              <option value="">{t('grants.selectUser')}</option>
              {users.filter(user => user.isActive &&
                user.userId.toLowerCase() !== currentUserId.toLowerCase())
                .map(user => <option key={user.userId} value={user.userId}>
                  {user.displayName || user.identityName}
                </option>)}
            </select>
          </label>
        )}
        {selectedUser && !pending && !error && (
          <div className="grant-current-list">
            <h3>{t('grants.existing')}</h3>
            {grants.length === 0
              ? <p>{t('grants.noExisting')}</p>
              : <ul>{grants.map(grant => {
                const role = roles.find(item => item.roleId === grant.roleId);
                return <li key={grant.grantId}>
                  {role?.name ?? grant.roleId}: {grant.scopeKind}
                  {grant.assetIds.length > 0 &&
                    ' (' + grant.assetIds.map(id =>
                      assets.find(asset => asset.id === id)?.name ?? id).join(', ') + ')'}
                </li>;
              })}</ul>}
            <button type="button" disabled={saving}
              onClick={() => setPending({
                subjectKind: 'User', userId: selectedUser.userId,
                roleId: '', scopeKind: 'ExactAssets', assetIds: [],
              })}>{t('grants.new')}</button>
          </div>
        )}
      </section>
      {selectedUser && pending && !pending.roleId && (
        <ScopedGrantEditor
          key={selectedUser.userId} user={selectedUser}
          roles={roles} permissions={permissions} assets={assets}
          allowAllAssets={true}
          onContinue={draft => setPending(draft)}
          onCancel={() => setPending(null)}
        />
      )}
      {selectedUser && pending && pending.roleId && preview && (
        <section className="panel grant-final-review" role="region"
          aria-label={t('grants.finalReview')}>
          <div className="panel-header"><h3>{t('grants.finalReview')}</h3></div>
          <p>{selectedUser.identityName}: {selectedRole?.name} / {pending.scopeKind}</p>
          <p>{t('grants.effective', { count: preview.effective.length })}</p>
          <ul>{preview.effective.map(key => <li key={key}>{key}</li>)}</ul>
          {preview.assetNames.length > 0 && <p>{preview.assetNames.join(', ')}</p>}
          <p className="access-warning">{t('grants.noRuntimeEffect')}</p>
          <div className="execution-detail-actions">
            <button type="button" className="secondary-button" disabled={saving}
              onClick={() => setPending(null)}>{t('access.cancel')}</button>
            <button type="button" disabled={saving}
              onClick={() => void save()}>
              {saving ? t('access.saving') : t('grants.saveDraft')}
            </button>
          </div>
        </section>
      )}
    </>
  );
}
