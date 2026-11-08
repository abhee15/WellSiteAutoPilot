import { ActionButton } from '../components/ActionButton';
import { ArrowRight } from 'lucide-react';
import { SearchInput } from '../components/SearchInput';
import { CheckboxField, SelectField } from '../components/FormFields';
import { Notice } from '../components/Notice';
import { useMemo, useState } from 'react';
import { t } from '../localization';
import type { PermissionDefinition, RoleRecord } from './roleCatalog';
import { GrantValidationError, previewGrant } from './scopedAccessGrants';
import type { AssetChoice, GrantDraft, ScopeKind } from './scopedAccessGrants';
import { grantAssetLabels } from './scopedAccessGrants';

type Props = {
  user: { userId: string; identityName: string; groupId?: string; directoryId?: string };
  roles: readonly RoleRecord[]; permissions: readonly PermissionDefinition[];
  assets: readonly AssetChoice[]; allowAllAssets: boolean;
  onContinue: (grant: GrantDraft) => void; onCancel: () => void;
};

/** Planning only: the parent must perform the final audited server mutation. */
export function ScopedGrantEditor({ user, roles, permissions, assets,
  allowAllAssets, onContinue, onCancel }: Props) {
  const [roleId, setRoleId] = useState('');
  const [scopeKind, setScopeKind] = useState<ScopeKind>('ExactAssets');
  const [assetIds, setAssetIds] = useState<string[]>([]);
  const [query, setQuery] = useState('');
  const [acknowledged, setAcknowledged] = useState(false);
  const role = roles.find(item => item.roleId === roleId) ?? null;
  const reviewedRoleRevision = role?.revision ?? 0;
  const draft = useMemo<GrantDraft>(() => ({ subjectKind: user.groupId ? 'Group' : 'User', userId: user.userId,
    ...(user.groupId ? { groupId: user.groupId, directoryId: user.directoryId } : {}),
    roleId, scopeKind, assetIds, expectedRoleRevision: reviewedRoleRevision }),
  [user.userId, user.groupId, user.directoryId, roleId, scopeKind, assetIds, reviewedRoleRevision]);
  const preview = useMemo(() => {
    try { return previewGrant(draft, role, permissions, assets, allowAllAssets); }
    catch (error) {
      if (!(error instanceof GrantValidationError)) throw error;
      return null;
    }
  }, [draft, role, permissions, assets, allowAllAssets]);
  const assetLabels = useMemo(() => grantAssetLabels(assets), [assets]);
  const matching = assets.filter(asset => asset.isActive &&
    (assetLabels.get(asset.id.toLowerCase()) ?? asset.name).toLocaleLowerCase().includes(query.trim().toLocaleLowerCase()))
    .sort((a, b) => a.name.localeCompare(b.name) || a.id.localeCompare(b.id));
  // Avoid rendering thousands of field Assets in an industrial tablet panel.
  // Selected Asset IDs remain stable when the search view is narrowed.
  const visible = matching.slice(0, 100);

  return (
    <section className="panel grant-editor" role="region"
      aria-label={t('grants.heading')}>
      <div className="panel-header">
        <h3>{t('grants.heading')}</h3>
        <ActionButton label={t('access.cancel')} onClick={onCancel} />
      </div>
      <p className="access-note access-intro">{t(user.groupId ? 'grants.forGroup' : 'grants.forUser', { name: user.identityName })}</p>
      <div className="grant-fields">
        <SelectField label={t('grants.role')} value={roleId} onChange={event => {
          setRoleId(event.target.value); setAcknowledged(false);
        }}>
          <option value="">{t('grants.chooseRole')}</option>
          {roles.filter(r => r.isActive).map(r =>
            <option key={r.roleId} value={r.roleId}>{r.name}</option>)}
        </SelectField>
        <SelectField label={t('grants.scope')} value={scopeKind} onChange={event => {
          setScopeKind(event.target.value as ScopeKind);
          setAssetIds([]); setAcknowledged(false);
        }}>
          <option value="ExactAssets">{t('grants.exact')}</option>
          <option value="DeploymentWide">{t('grants.deployment')}</option>
          {allowAllAssets && <option value="AllAssets">{t('grants.allAssets')}</option>}
        </SelectField>
      </div>
      {scopeKind === 'ExactAssets' && (
        <fieldset className="grant-assets">
          <legend>{t('grants.assets')}</legend>
          <label>{t('grants.search')}
            <SearchInput controlSize="large" value={query} maxLength={120} placeholder={t('grants.searchPlaceholder')}
              onChange={event => setQuery(event.target.value)} />
          </label>
          {matching.length > visible.length &&
            <p className="access-note">{t('grants.refineSearch',
              { count: matching.length })}</p>}
          <div className="grant-asset-list">
            {visible.map(asset => (
              <div className="grant-asset-row" key={asset.id}>
                <CheckboxField label={assetLabels.get(asset.id.toLowerCase()) ?? asset.name}
                  aria-describedby={'grant-asset-' + asset.id} checked={assetIds.includes(asset.id)}
                  onChange={event => {
                    setAssetIds(prev => event.target.checked ?
                      [...new Set([...prev, asset.id])] : prev.filter(id => id !== asset.id));
                    setAcknowledged(false);
                  }} />
                <small id={'grant-asset-' + asset.id}>{asset.id}</small>
              </div>
            ))}
            {visible.length === 0 && <p>{t('grants.none')}</p>}
          </div>
        </fieldset>
      )}
      <Notice><div className="grant-preview">
        <strong>{t('grants.preview')}</strong>
        {preview ? (
          <>
            <p>{t('grants.effective', { count: preview.effective.length })}</p>
            <ul>{preview.effective.map(key => <li key={key}>{key}</li>)}</ul>
            {preview.excluded.length > 0 &&
              <p>{t('grants.excluded', { count: preview.excluded.length })}</p>}
            {preview.assetNames.length > 0 &&
              <p>{preview.assetNames.join(', ')}</p>}
            {preview.privileged &&
              <p className="access-warning">{t('grants.privileged')}</p>}
          </>
        ) : <p>{t('grants.incomplete')}</p>}
      </div></Notice>
      <p className="access-note access-intro">{t('grants.noSave')}</p>
      <div className="grant-confirm">
        <CheckboxField label={t('grants.confirm')} checked={acknowledged} disabled={!preview}
          onChange={event => setAcknowledged(event.target.checked)} />
      </div>
      <div className="execution-detail-actions grant-actions">
        <ActionButton label={t('grants.continue')} icon={ArrowRight} variant="primary" controlSize="large"
          disabled={!preview || !acknowledged} onClick={() => onContinue(draft)} />
      </div>
    </section>
  );
}
