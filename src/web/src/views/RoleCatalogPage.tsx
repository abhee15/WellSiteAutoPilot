import { ActionButton } from '../components/ActionButton';
import { RefreshCw } from 'lucide-react';
import { SearchInput } from '../components/SearchInput';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { t } from '../localization';
import type { MessageKey } from '../localization';
import { loadRoleCatalog, RoleCatalogError, roleChanged, saveRole } from './roleCatalog';
import type { PermissionDefinition, RoleErrorKind, RoleMutation, RoleRecord } from './roleCatalog';
import { LoadingIndicator } from '../components/LoadingIndicator';

const labels: Record<string, MessageKey> = {
  'assets.read': 'roles.permissionAssetsRead',
  'assets.manage': 'roles.permissionAssetsManage',
  'logic.read': 'roles.permissionLogicRead',
  'logic.manage': 'roles.permissionLogicManage',
  'configured-logic.read': 'roles.permissionConfiguredRead',
  'configured-logic.manage': 'roles.permissionConfiguredManage',
  'executions.read': 'roles.permissionExecutionsRead',
  'executions.request': 'roles.permissionExecutionsRequest',
  'recommendations.read': 'roles.permissionRecommendationsRead',
  'recommendations.decide': 'roles.permissionRecommendationsDecide',
  'security.manage': 'roles.permissionSecurityManage',
  'asset-types.manage': 'roles.permissionAssetTypesManage',
  'asset-types.hide': 'roles.permissionAssetTypesHide',
  'connections.read': 'roles.permissionConnectionsRead',
  'connections.manage': 'roles.permissionConnectionsManage',
  'asset-views.create': 'roles.permissionAssetViewsCreate',
  'asset-views.edit': 'roles.permissionAssetViewsEdit',
  'asset-views.delete': 'roles.permissionAssetViewsDelete',
};
const categories: Record<string, MessageKey> = {
  Assets: 'roles.categoryAssets',
  Logic: 'roles.categoryLogic',
  ConfiguredLogic: 'roles.categoryConfigured',
  Executions: 'roles.categoryExecutions',
  Recommendations: 'roles.categoryRecommendations',
  Security: 'roles.categorySecurity',
  AssetTypes: 'roles.categoryAssetTypes',
  Connections: 'roles.categoryConnections',
  AssetViews: 'roles.categoryAssetViews',
};
function errorLabel(kind: RoleErrorKind): string {
  switch (kind) {
    case 'accessDenied': return t('roles.denied');
    case 'selfAccess': return t('security.selfAccessChange');
    case 'groupProofRequired': return t('security.groupProofRequired');
    case 'notFound': return t('roles.notFound');
    case 'conflict': return t('roles.conflict');
    case 'invalid': return t('roles.invalid');
    case 'invalidResponse': return t('roles.invalidResponse');
    case 'unavailable': return t('roles.unavailable');
  }
}

type EditorProps = {
  original: RoleRecord | null;
  template: RoleRecord | null;
  permissions: PermissionDefinition[];
  onSaved: (role: RoleRecord) => void;
  onCancel: () => void;
  onFailure: (kind: RoleErrorKind) => void;
};

export function RoleDefinitionEditor({
  original, template, permissions, onSaved, onCancel, onFailure,
}: EditorProps) {
  const [name, setName] = useState(() => original?.name ??
    (template ? template.name + ' Copy' : ''));
  const [description, setDescription] = useState(() => original?.description ??
    template?.description ?? '');
  const [enabled, setEnabled] = useState(() => original?.isActive ?? true);
  const [keys, setKeys] = useState<string[]>(() =>
    [...(original?.permissions ?? template?.permissions ?? [])]);
  const [acknowledged, setAcknowledged] = useState(false);
  const [busy, setBusy] = useState(false);
  const pending = useRef<AbortController | null>(null);
  useEffect(() => () => { pending.current?.abort(); }, []);

  const selected = useMemo(() => new Set(keys), [keys]);
  const groups = useMemo(() => {
    const result = new Map<string, PermissionDefinition[]>();
    for (const entry of permissions) {
      const current = result.get(entry.category) ?? [];
      current.push(entry);
      result.set(entry.category, current);
    }
    return [...result.entries()];
  }, [permissions]);

  const mutation: RoleMutation = { name, description, isActive: enabled, permissions: keys };
  const dirty = original ? roleChanged(original, mutation) : true;
  const canSave = !busy && name.trim().length >= 2 &&
    name.trim().length <= 80 && description.length <= 500 &&
    acknowledged && dirty && !original?.isSystem;
  const elevated = permissions.some(p => p.isPrivileged && selected.has(p.key));

  const toggle = (key: string, checked: boolean) => {
    setKeys(current => checked ? [...current.filter(x => x !== key), key] :
      current.filter(x => x !== key));
    setAcknowledged(false);
  };

  const submit = async () => {
    if (!canSave || pending.current) return;
    const controller = new AbortController();
    pending.current = controller;
    setBusy(true);
    try {
      const saved = await saveRole(
        original, mutation, permissions.map(p => p.key), acknowledged, controller.signal);
      if (!controller.signal.aborted) onSaved(saved);
    } catch (error) {
      if (!controller.signal.aborted) onFailure(
        error instanceof RoleCatalogError ? error.kind : 'unavailable');
    } finally {
      if (pending.current === controller) {
        pending.current = null;
        setBusy(false);
      }
    }
  };

  return (
    <section className="panel role-editor" role="region" aria-label={t('roles.editor')}>
      <div className="panel-header">
        <h3>{original ? t('roles.editHeading') : t('roles.createHeading')}</h3>
        <button className="secondary-button" type="button" onClick={onCancel}
          disabled={busy}>{t('roles.cancel')}</button>
      </div>
      <p className="access-note access-intro">{t('roles.inactiveUntilGrants')}</p>
      {original && !original.isSystem &&
        <p className="access-note access-intro">{t('roles.assignedRoleWarning')}</p>}
      <div className="role-fields">
        <label><span>{t('roles.name')}</span>
          <input value={name} maxLength={80} disabled={busy}
            onChange={event => { setName(event.target.value); setAcknowledged(false); }} />
        </label>
        <label><span>{t('roles.description')}</span>
          <textarea value={description} maxLength={500} rows={3} disabled={busy}
            onChange={event => { setDescription(event.target.value); setAcknowledged(false); }} />
        </label>
        {original && <label className="access-confirm">
          <input type="checkbox" checked={enabled} disabled={busy}
            onChange={event => { setEnabled(event.target.checked); setAcknowledged(false); }} />
          {t('roles.enabled')}
        </label>}
      </div>

      <fieldset className="role-permissions" disabled={busy}>
        <legend>{t('roles.permissions')}</legend>
        {groups.map(([category, choices]) => (
          <div className="role-permission-group" key={category}>
            <h4>{categories[category] ? t(categories[category]) : category}</h4>
            {choices.map(permission => (
              <label key={permission.key} className="access-confirm">
                <input type="checkbox" checked={selected.has(permission.key)}
                  onChange={event => toggle(permission.key, event.target.checked)} />
                <span><strong>{labels[permission.key] ?
                  t(labels[permission.key]) : permission.key}</strong>
                  <small>{permission.key} · {permission.scopeKind === 'DeploymentWide'
                    ? t('roles.deploymentWide') : t('roles.assetBound')}</small>
                </span>
              </label>
            ))}
          </div>
        ))}
      </fieldset>
      {elevated && <p className="access-warning" role="note">
        {t('roles.privilegedWarning')}
      </p>}
      <div className="access-review">
        <strong>{t('roles.reviewHeading')}</strong>
        <p>{t('roles.reviewName', { name: name.trim() || '—' })}</p>
        <p>{t('roles.reviewPermissions', { count: keys.length })}</p>
        <p>{original ? t('roles.reviewRevision', { revision: original.revision })
          : t('roles.reviewNew')}</p>
      </div>
      <label className="access-confirm">
        <input type="checkbox" checked={acknowledged} disabled={busy}
          onChange={event => setAcknowledged(event.target.checked)} />
        {t('roles.confirm')}
      </label>
      <button type="button" className="primary-button"
        disabled={!canSave} onClick={() => void submit()}>
        {busy ? t('roles.saving') : t('roles.save')}
      </button>
    </section>
  );
}

type EditorState =
  | { kind: 'new'; template: RoleRecord | null }
  | { kind: 'edit'; original: RoleRecord }
  | null;

export function RoleCatalogPage() {
  const [roles, setRoles] = useState<RoleRecord[]>([]);
  const [permissions, setPermissions] = useState<PermissionDefinition[]>([]);
  const [loading, setLoading] = useState(true);
  const [query, setQuery] = useState('');
  const [error, setError] = useState<RoleErrorKind | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [editor, setEditor] = useState<EditorState>(null);
  const controller = useRef<AbortController | null>(null);

  const reload = useCallback(async () => {
    controller.current?.abort();
    const next = new AbortController();
    controller.current = next;
    setLoading(true);
    setError(null);
    try {
      const result = await loadRoleCatalog(next.signal);
      if (next.signal.aborted) return;
      setRoles(result.roles);
      setPermissions(result.permissions);
    } catch (reason) {
      if (next.signal.aborted) return;
      setRoles([]);
      setPermissions([]);
      setEditor(null);
      setError(reason instanceof RoleCatalogError ? reason.kind : 'unavailable');
    } finally {
      if (controller.current === next) {
        controller.current = null;
        setLoading(false);
      }
    }
  }, []);

  useEffect(() => {
    void reload();
    return () => { controller.current?.abort(); controller.current = null; };
  }, [reload]);

  const visible = useMemo(() => {
    const term = query.trim().toLocaleLowerCase();
    return roles.filter(role => !term ||
      role.name.toLocaleLowerCase().includes(term) ||
      role.key.toLocaleLowerCase().includes(term))
      .sort((a, b) => a.name.localeCompare(b.name));
  }, [roles, query]);

  return (
    <>
      <section className="panel">
        <div className="panel-header">
          <h2>{t('roles.heading')}</h2>
          <div className="role-actions">
            <ActionButton icon={RefreshCw} label={t('roles.refresh')}
              disabled={loading} onClick={() => void reload()} />
            <button type="button" className="primary-button"
              disabled={loading || !!error} onClick={() => {
                setEditor({ kind: 'new', template: null }); setNotice(null);
              }}>{t('roles.create')}</button>
          </div>
        </div>
        <p className="access-note access-intro">{t('roles.intro')}</p>
        {error && <p role="alert" className="execution-detail-error access-intro">
          {errorLabel(error)}</p>}
        {notice && <p role="status" className="access-intro">{notice}</p>}
        {loading ? <LoadingIndicator label={t('roles.loading')} /> : !error && (
          <>
            <label className="role-search"><span>{t('roles.search')}</span>
              <SearchInput value={query} maxLength={120}
                onChange={event => setQuery(event.target.value)} />
            </label>
            <div className="table-wrap">
              <table className="activity-table" aria-label={t('roles.heading')}>
                <thead><tr>
                  <th scope="col">{t('roles.name')}</th>
                  <th scope="col">{t('roles.type')}</th>
                  <th scope="col">{t('roles.permissions')}</th>
                  <th scope="col">{t('roles.status')}</th>
                  <th scope="col">{t('roles.actions')}</th>
                </tr></thead>
                <tbody>
                  {visible.map(role => (
                    <tr key={role.roleId}>
                      <td><strong>{role.name}</strong><small>{role.description}</small></td>
                      <td>{role.isSystem ? t('roles.systemRole') : t('roles.customRole')}</td>
                      <td>{role.permissions.length}</td>
                      <td>{role.isActive ? t('roles.active') : t('roles.inactive')}</td>
                      <td>
                        <button className="secondary-button" type="button"
                          onClick={() => { setEditor({ kind: 'new', template: role }); setNotice(null); }}>
                          {t('roles.clone')}
                        </button>
                        {!role.isSystem && <button className="secondary-button" type="button"
                          onClick={() => { setEditor({ kind: 'edit', original: role }); setNotice(null); }}>
                          {t('roles.edit')}
                        </button>}
                      </td>
                    </tr>
                  ))}
                  {visible.length === 0 && <tr><td colSpan={5} className="empty-state">
                    {t('roles.empty')}</td></tr>}
                </tbody>
              </table>
            </div>
          </>
        )}
      </section>
      {editor && (
        <RoleDefinitionEditor
          key={editor.kind === 'edit' ? editor.original.roleId :
            'new-' + (editor.template?.roleId ?? 'empty')}
          original={editor.kind === 'edit' ? editor.original : null}
          template={editor.kind === 'new' ? editor.template : null}
          permissions={permissions}
          onCancel={() => setEditor(null)}
          onSaved={() => {
            setEditor(null); setNotice(t('roles.saved')); void reload();
          }}
          onFailure={kind => {
            setEditor(null); setNotice(errorLabel(kind)); void reload();
          }}
        />
      )}
    </>
  );
}
