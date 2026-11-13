import { DataTable } from '../components/DataTable';
import { ActionButton } from '../components/ActionButton';
import { CheckboxField, FormActions, TextAreaField, TextField } from '../components/FormFields';
import { Notice } from '../components/Notice';
import './roleCatalog.css';
import { EmptyRow, PanelHeader } from '../components/common';
import { RefreshCw } from 'lucide-react';
import { FilterToolbar } from '../components/FilterToolbar';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { t } from '../localization';
import type { MessageKey } from '../localization';
import { loadRoleCatalog, RoleCatalogError, roleChanged, saveRole } from './roleCatalog';
import type { PermissionDefinition, RoleErrorKind, RoleMutation, RoleRecord } from './roleCatalog';
import { PanelLoading } from '../components/LoadingIndicator';
import { Status } from '../components/Status';
import { useSplitPaneFocus } from '../components/useSplitPaneFocus';
import './splitLayout.css';
import { useUnsavedChangesGuard } from '../navigationGuard';

const labels: Record<string, MessageKey> = {
  'assets.read': 'roles.permissionAssetsRead',
  'assets.manage': 'roles.permissionAssetsManage',
  'logic.read': 'roles.permissionLogicRead',
  'logic.authoring.manage': 'roles.permissionGuidedDraftManage',
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
  'control.manage': 'roles.permissionControlManage',
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
  Control: 'roles.categoryControl',
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
  // What the editor opened with (the role, a copy of a template, or blank). Reverting an edit makes it clean again.
  const snapshot = (value: RoleMutation) => JSON.stringify([value.name, value.description, value.isActive, [...value.permissions].sort()]);
  const opened = useRef<string | null>(null);
  if (opened.current === null) opened.current = snapshot(mutation);
  const markSaved = useUnsavedChangesGuard(snapshot(mutation) !== opened.current);
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
      if (!controller.signal.aborted) { markSaved(); onSaved(saved); }
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
      <PanelHeader level={3} title={original ? t('roles.editHeading') : t('roles.createHeading')} />
      <p className="access-note access-intro">{t('roles.inactiveUntilGrants')}</p>
      {original && !original.isSystem &&
        <p className="access-note access-intro">{t('roles.assignedRoleWarning')}</p>}
      <div className="role-fields">
        <TextField label={t('roles.name')} value={name} maxLength={80} disabled={busy} placeholder={t('roles.namePlaceholder')}
          onChange={event => { setName(event.target.value); setAcknowledged(false); }} />
        <TextAreaField label={t('roles.description')} value={description} maxLength={500} rows={3} disabled={busy} placeholder={t('roles.descriptionPlaceholder')}
          onChange={event => { setDescription(event.target.value); setAcknowledged(false); }} />
        {original && <CheckboxField label={t('roles.enabled')} checked={enabled} disabled={busy}
          onChange={event => { setEnabled(event.target.checked); setAcknowledged(false); }} />}
      </div>

      <fieldset className="role-permissions" disabled={busy}>
        <legend>{t('roles.permissions')}</legend>
        {groups.map(([category, choices]) => (
          <div className="role-permission-group" key={category}>
            <h4>{categories[category] ? t(categories[category]) : category}</h4>
            {choices.map(permission => (
              <CheckboxField key={permission.key} checked={selected.has(permission.key)}
                label={labels[permission.key] ? t(labels[permission.key]) : permission.key}
                helper={`${permission.key} · ${permission.scopeKind === 'DeploymentWide' ? t('roles.deploymentWide') : t('roles.assetBound')}`}
                onChange={event => toggle(permission.key, event.target.checked)} />
            ))}
          </div>
        ))}
      </fieldset>
      {elevated && <Notice tone="warning" announce={false}>{t('roles.privilegedWarning')}</Notice>}
      <div className="access-review">
        <strong>{t('roles.reviewHeading')}</strong>
        <p>{t('roles.reviewName', { name: name.trim() || '—' })}</p>
        <p>{t('roles.reviewPermissions', { count: keys.length })}</p>
        <p>{original ? t('roles.reviewRevision', { revision: original.revision })
          : t('roles.reviewNew')}</p>
      </div>
      <CheckboxField label={t('roles.confirm')} checked={acknowledged} disabled={busy}
        onChange={event => setAcknowledged(event.target.checked)} />
      <FormActions>
        <ActionButton label={t('roles.cancel')} onClick={onCancel} disabled={busy} />
        <ActionButton label={busy ? t('roles.saving') : t('roles.save')} variant="primary" disabled={!canSave} onClick={() => void submit()} />
      </FormActions>
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

  const editorKey = editor ? (editor.kind === 'edit' ? editor.original.roleId : 'new-' + (editor.template?.roleId ?? 'empty')) : null;
  const splitFocus = useSplitPaneFocus(editorKey);
  return (
    <div {...splitFocus} className="split-layout runs-layout roles-layout" data-has-selection={editor !== null}>
    <div className="split-panes">
      <section className="panel split-browse">
        <PanelHeader title={t('roles.heading')} actions={<>
          <ActionButton icon={RefreshCw} iconOnly label={t('roles.refresh')}
            disabled={loading} onClick={() => void reload()} />
          <ActionButton label={t('roles.create')} variant="primary" disabled={loading || !!error}
            onClick={() => { setEditor({ kind: 'new', template: null }); setNotice(null); }} />
        </>} />
        <p className="access-note access-intro">{t('roles.intro')}</p>
        {error && <Notice tone="error">{errorLabel(error)}</Notice>}
        {notice && <Notice>{notice}</Notice>}
        {loading ? <PanelLoading label={t('roles.loading')} /> : !error && (
          <>
            <FilterToolbar label={t('roles.controls')}
              search={{ value: query, label: t('roles.search'), placeholder: t('roles.searchPlaceholder'), onChange: setQuery }}
              isFiltered={query.trim() !== ''} onClear={() => setQuery('')}
              note={query.trim() !== '' ? t('roles.count', { shown: visible.length, total: roles.length }) : undefined} />
            <DataTable label={t('roles.heading')}>
                <thead><tr>
                  <th scope="col">{t('roles.name')}</th>
                  <th scope="col" className="run-col-secondary">{t('roles.type')}</th>
                  <th scope="col" className="run-col-secondary">{t('roles.permissions')}</th>
                  <th scope="col">{t('roles.status')}</th>
                  <th scope="col">{t('roles.actions')}</th>
                </tr></thead>
                <tbody>
                  {visible.map(role => (
                    <tr key={role.roleId} data-selected={editor?.kind === 'edit' && editor.original.roleId === role.roleId}>
                      <td><strong>{role.name}</strong><small>{role.description}</small></td>
                      <td className="run-col-secondary">{role.isSystem ? t('roles.systemRole') : t('roles.customRole')}</td>
                      <td className="run-col-secondary">{role.permissions.length}</td>
                      <td><Status value={role.isActive ? 'Active' : 'Inactive'}
                        label={role.isActive ? t('roles.active') : t('roles.inactive')} /></td>
                      <td><div className="row-actions">
                        <ActionButton label={t('roles.clone')}
                          onClick={() => { setEditor({ kind: 'new', template: role }); setNotice(null); }} />
                        {!role.isSystem && <ActionButton label={t('roles.edit')}
                          onClick={() => { setEditor({ kind: 'edit', original: role }); setNotice(null); }} />}
                      </div></td>
                    </tr>
                  ))}
                  {visible.length === 0 && <EmptyRow columns={5}
                    action={roles.length > 0 && query.trim() !== '' ? <ActionButton label={t('views.clearFilters')} onClick={() => setQuery('')} /> : undefined}>
                    {t('roles.empty')}</EmptyRow>}
                </tbody>
              </DataTable>
          </>
        )}
      </section>
      <div className="split-selected">
        <ActionButton label={t('roles.backToList')} className="split-back" onClick={() => setEditor(null)} />
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
      </div>
    </div>
    </div>
  );
}
