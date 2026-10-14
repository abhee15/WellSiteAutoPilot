import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { SecurityUserResponse } from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { formatDetailTimestamp } from './executionDetail';
import {
  ACCESS_ROLES, AccessError, accessChanged, loadSecurityUsers, parseScopeIds,
  replaceUserAccess,
} from './accessManagement';
import type { AccessErrorKind, AccessRole } from './accessManagement';

function errorText(kind: AccessErrorKind): string {
  switch (kind) {
    case 'accessDenied': return t('access.denied');
    case 'notFound': return t('access.notFound');
    case 'conflict': return t('access.conflict');
    case 'invalid': return t('access.invalid');
    case 'invalidResponse': return t('access.invalidResponse');
    case 'unavailable': return t('access.unavailable');
  }
}

type EditorProps = {
  user: SecurityUserResponse;
  currentUserId: string;
  onCancel: () => void;
  onSaved: (updated: SecurityUserResponse) => void;
  onRequireReload: (kind: AccessErrorKind) => void;
};

/** Edits stay local until an explicit confirmed, non-retried PUT request. */
export function AccessChangeEditor({
  user, currentUserId, onCancel, onSaved, onRequireReload,
}: EditorProps) {
  const [roles, setRoles] = useState<AccessRole[]>(() =>
    ACCESS_ROLES.filter(role => user.roles?.includes(role)));
  const [scopeText, setScopeText] = useState(() => (user.assetScopeIds ?? []).join('\n'));
  const [acknowledged, setAcknowledged] = useState(false);
  const [saving, setSaving] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const activeMutation = useRef<AbortController | null>(null);
  useEffect(() => () => { activeMutation.current?.abort(); }, []);

  const scopes = useMemo(() => {
    try { return parseScopeIds(scopeText); } catch { return null; }
  }, [scopeText]);
  const self = user.userId?.toLowerCase() === currentUserId.toLowerCase();
  const dirty = scopes !== null && accessChanged(user, roles, scopes);
  const canSave = !self && !saving && dirty && acknowledged && scopes !== null;

  const changeRoles = (role: AccessRole, selected: boolean) => {
    setRoles(prev => selected ? [...prev, role] : prev.filter(item => item !== role));
    setAcknowledged(false);
    setNotice(null);
  };
  const save = async () => {
    if (!canSave || activeMutation.current) return;
    const controller = new AbortController();
    activeMutation.current = controller;
    setSaving(true);
    setNotice(null);
    try {
      const updated = await replaceUserAccess(
        user, currentUserId, roles, scopeText, acknowledged, controller.signal);
      if (!controller.signal.aborted) onSaved(updated);
    } catch (reason) {
      if (controller.signal.aborted) return;
      const kind: AccessErrorKind = reason instanceof AccessError
        ? reason.kind : 'unavailable';
      // A failed or uncertain write must be reconciled with the server before
      // trying again. Never automatically resubmit an access change.
      onRequireReload(kind);
    } finally {
      if (activeMutation.current === controller) {
        activeMutation.current = null;
        setSaving(false);
      }
    }
  };

  return (
    <section className="access-editor execution-detail" role="region"
      aria-label={t('access.editFor', { name: user.identityName ?? '—' })}>
      <div className="execution-detail-header">
        <div>
          <h3>{t('access.editorHeading')}</h3>
          <p>{user.identityName} · {user.displayName ?? t('access.noDisplayName')}</p>
        </div>
        <button type="button" className="secondary-button" onClick={onCancel}
          disabled={saving}>{t('access.cancel')}</button>
      </div>
      <p className="access-warning" role="note">{t('access.replaceWarning')}</p>
      <fieldset className="access-role-options" disabled={saving || self}>
        <legend>{t('access.roles')}</legend>
        {ACCESS_ROLES.map(role => (
          <label key={role}>
            <input type="checkbox" checked={roles.includes(role)}
              onChange={event => changeRoles(role, event.target.checked)} />
            {role}
          </label>
        ))}
      </fieldset>
      <label className="access-scope-field">
        <span>{t('access.scopes')}</span>
        <textarea rows={5} value={scopeText} disabled={saving || self}
          placeholder={t('access.scopePlaceholder')}
          onChange={event => {
            setScopeText(event.target.value);
            setAcknowledged(false);
            setNotice(null);
          }} />
      </label>
      <p className="access-note">{t('access.scopeRule')}</p>
      {roles.includes('Admin') && <p className="access-warning" role="note">
        {t('access.adminScopeWarning')}
      </p>}
      {scopes === null && <p className="execution-detail-error" role="alert">
        {t('access.invalidScopes')}
      </p>}
      {self && <p role="alert" className="execution-detail-error">{t('access.selfChange')}</p>}
      <div className="access-review">
        <strong>{t('access.reviewHeading')}</strong>
        <p>{t('access.reviewTarget', { name: user.identityName ?? '—' })}</p>
        <p>{t('access.reviewRoles', { value: roles.join(', ') || t('access.noRoles') })}</p>
        <p>{t('access.reviewScopes', { count: scopes?.length ?? 0 })}</p>
      </div>
      <label className="access-confirm">
        <input type="checkbox" checked={acknowledged} disabled={saving || self || !dirty}
          onChange={event => setAcknowledged(event.target.checked)} />
        {t('access.confirm')}
      </label>
      {notice && <p role="alert" className="execution-detail-error">{notice}</p>}
      <div className="execution-detail-actions">
        <button type="button" disabled={!canSave}
          onClick={() => void save()}>
          {saving ? t('access.saving') : t('access.submit')}
        </button>
      </div>
    </section>
  );
}

export function AccessManagementPage({ currentUserId }: { currentUserId: string }) {
  const [users, setUsers] = useState<SecurityUserResponse[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<AccessErrorKind | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [query, setQuery] = useState('');
  const activeRefresh = useRef<AbortController | null>(null);

  const reload = useCallback(async () => {
    activeRefresh.current?.abort();
    const controller = new AbortController();
    activeRefresh.current = controller;
    setUsers([]);
    setSelectedId(null);
    setIsLoading(true);
    setError(null);
    try {
      const data = await loadSecurityUsers(controller.signal);
      if (!controller.signal.aborted) setUsers(data);
    } catch (reason) {
      if (controller.signal.aborted) return;
      setError(reason instanceof AccessError ? reason.kind : 'unavailable');
    } finally {
      if (activeRefresh.current === controller) {
        activeRefresh.current = null;
        setIsLoading(false);
      }
    }
  }, []);

  useEffect(() => {
    void reload();
    return () => {
      activeRefresh.current?.abort();
      activeRefresh.current = null;
    };
  }, [reload, currentUserId]);

  const selected = users.find(user => user.userId === selectedId);
  const visible = useMemo(() => {
    const search = query.trim().toLocaleLowerCase();
    return users.filter(user =>
      !search ||
      user.identityName?.toLocaleLowerCase().includes(search) ||
      user.displayName?.toLocaleLowerCase().includes(search),
    ).sort((a, b) => (a.identityName ?? '').localeCompare(b.identityName ?? ''));
  }, [query, users]);

  return (
    <>
      <section className="panel">
        <div className="panel-header">
          <div>
            <p className="eyebrow">{t('access.eyebrow')}</p>
            <h2>{t('access.users')}</h2>
          </div>
          <button type="button" className="secondary-button" onClick={() => void reload()}
            disabled={isLoading}>{t('access.refresh')}</button>
        </div>
        <p className="access-note access-intro">{t('access.listNote')}</p>
        {error && <p role="alert" className="execution-detail-error access-intro">
          {errorText(error)}
        </p>}
        {notice && <p role="status" className="access-intro">{notice}</p>}
        {isLoading && <p className="status-loading access-intro" role="status">
          {t('access.loading')}
        </p>}
        {!isLoading && !error && (
          <>
            <div className="data-view-controls">
              <label>
                <span>{t('access.search')}</span>
                <input type="search" value={query} maxLength={120}
                  onChange={event => setQuery(event.target.value)} />
              </label>
            </div>
            <div className="table-wrap">
              <table className="activity-table" aria-label={t('access.users')}>
                <thead>
                  <tr>
                    <th scope="col">{t('access.identity')}</th>
                    <th scope="col">{t('access.roles')}</th>
                    <th scope="col">{t('access.scopesHeading')}</th>
                    <th scope="col">{t('access.lastSeen')}</th>
                    <th scope="col">{t('access.actions')}</th>
                  </tr>
                </thead>
                <tbody>
                  {visible.map(user => (
                    <tr key={user.userId}>
                      <td><strong>{user.displayName || user.identityName}</strong>
                        <small>{user.identityName}</small></td>
                      <td>{user.roles?.join(', ') || t('access.noRoles')}</td>
                      <td>{user.roles?.includes('Admin')
                        ? t('access.allAssets') : String(user.assetScopeIds?.length ?? 0)}</td>
                      <td>{formatDetailTimestamp(user.lastSeenAtUtc)}</td>
                      <td><button type="button" className="secondary-button"
                        disabled={!user.isActive || user.userId?.toLowerCase() === currentUserId.toLowerCase()}
                        onClick={() => { setSelectedId(user.userId ?? null); setNotice(null); }}>
                        {t('access.review')}
                      </button></td>
                    </tr>
                  ))}
                  {visible.length === 0 && <tr><td colSpan={5} className="empty-state">
                    {t('access.noMatches')}
                  </td></tr>}
                </tbody>
              </table>
            </div>
          </>
        )}
      </section>
      {selected && (
        <AccessChangeEditor key={selected.userId} user={selected}
          currentUserId={currentUserId} onCancel={() => setSelectedId(null)}
          onSaved={updated => {
            setUsers(previous => previous.map(user =>
              user.userId === updated.userId ? updated : user));
            setSelectedId(null);
            setNotice(t('access.saved'));
          }}
          onRequireReload={kind => {
            setNotice(errorText(kind));
            void reload();
          }} />
      )}
    </>
  );
}
