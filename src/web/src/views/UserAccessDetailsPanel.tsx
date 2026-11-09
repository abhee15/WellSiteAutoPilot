import { useEffect, useRef, useState } from 'react';
import { RefreshCw } from 'lucide-react';
import { ActionButton } from '../components/ActionButton';
import { CloseButton } from '../components/CloseButton';
import { LoadingIndicator } from '../components/LoadingIndicator';
import { Status } from '../components/Status';
import { t } from '../localization';
import { Notice } from '../components/Notice';
import { formatDetailTimestamp } from './executionDetail';
import { loadGrants, GrantApiError } from './grantAdministration';
import type { PersistedGrant } from './grantAdministration';
import { loadRoleCatalog, RoleCatalogError } from './roleCatalog';
import type { RoleRecord } from './roleCatalog';
import { loadGrantAssetDirectory } from './grantAssetDirectory';
import type { AssetChoice } from './scopedAccessGrants';
import type { VerifiedSecurityUser } from './accessManagement';

type DetailState = {
  grants: PersistedGrant[];
  roles: RoleRecord[];
  assets: AssetChoice[];
  namesIncomplete: boolean;
};

function grantScopeLabel(scope: PersistedGrant['scopeKind']): string {
  return t(scope === 'ExactAssets' ? 'grants.exact'
    : scope === 'AllAssets' ? 'grants.allAssets' : 'grants.deployment');
}

/**
 * Read-only inspector. The grant endpoint is authoritative and rechecks administrator privileges.
 * Role and asset directories only enrich names; missing names never become an access verdict.
 * This does not evaluate this user's directory memberships or effective permissions.
 */
export function UserAccessDetailsPanel({ user, canManage, onManage, onClose, onAccessDenied }: {
  user: VerifiedSecurityUser;
  canManage: boolean;
  onManage: (user: VerifiedSecurityUser) => void;
  onClose: () => void;
  onAccessDenied: () => void;
}) {
  const accessDenied = useRef(onAccessDenied);
  accessDenied.current = onAccessDenied;
  const [reload, setReload] = useState(0);
  const [data, setData] = useState<DetailState | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);

  useEffect(() => {
    const abort = new AbortController();
    setLoading(true); setError(false); setData(null);
    const read = async () => {
      try {
        const grants = await loadGrants(user.userId, abort.signal);
        if (abort.signal.aborted) return;
        const [roleResult, assetResult] = await Promise.allSettled([
          grants.length ? loadRoleCatalog(abort.signal) : Promise.resolve({ roles: [] as RoleRecord[] }),
          grants.some(grant => grant.scopeKind === 'ExactAssets')
            ? loadGrantAssetDirectory(abort.signal) : Promise.resolve([] as AssetChoice[]),
        ]);
        if (abort.signal.aborted) return;
        if (roleResult.status === 'rejected' && roleResult.reason instanceof RoleCatalogError &&
            roleResult.reason.kind === 'accessDenied') {
          accessDenied.current();
          return;
        }
        setData({
          grants,
          roles: roleResult.status === 'fulfilled' ? roleResult.value.roles : [],
          assets: assetResult.status === 'fulfilled' ? assetResult.value : [],
          namesIncomplete: roleResult.status !== 'fulfilled' || assetResult.status !== 'fulfilled',
        });
      } catch (cause) {
        if (abort.signal.aborted) return;
        setData(null);
        if (cause instanceof GrantApiError && (cause.kind === 'denied' || cause.kind === 'notFound')) {
          accessDenied.current();
          return;
        }
        setError(true);
      } finally {
        if (!abort.signal.aborted) setLoading(false);
      }
    };
    void read();
    return () => abort.abort();
  }, [user.userId, reload]);

  const roleById = new Map((data?.roles ?? []).map(role => [role.roleId.toLowerCase(), role]));
  const assetById = new Map((data?.assets ?? []).map(asset => [asset.id.toLowerCase(), asset]));
  const ordered = [...(data?.grants ?? [])].sort((a, b) =>
    Number(b.isActive) - Number(a.isActive) || a.roleId.localeCompare(b.roleId));

  return <div className="access-user-detail">
    <header className="access-user-detail-header">
      <div>
        <h3>{user.displayName || user.identityName}</h3>
        {user.displayName && <small>{user.identityName}</small>}
      </div>
      <div className="access-user-detail-header-actions">
        <ActionButton icon={RefreshCw} iconOnly label={t('access.refreshDetails')}
          disabled={loading} onClick={() => setReload(n => n + 1)} />
        <CloseButton label={t('access.closeDetails')} onClick={onClose} />
      </div>
    </header>
    <div className="access-user-detail-body">
      <section className="access-user-detail-section" aria-label={t('access.accountDetails')}>
        <h4>{t('access.accountDetails')}</h4>
        <dl className="access-user-facts">
          <div><dt>{t('session.account')}</dt><dd><Status value={user.isActive ? 'Active' : 'Inactive'}
            label={user.isActive ? t('session.active') : t('session.inactive')} /></dd></div>
          <div><dt>{t('access.lastSeen')}</dt><dd>{formatDetailTimestamp(user.lastSeenAtUtc)}</dd></div>
          <div><dt>{t('access.identity')}</dt><dd>{user.identityName}</dd></div>
        </dl>
      </section>

      <section className="access-user-detail-section" aria-label={t('access.directAssignments')}>
        <h4>{t('access.directAssignments')}</h4>
        {data && <p className="access-user-note">{t('access.assignmentRecordCount', { count: ordered.length })}</p>}
        {loading && <LoadingIndicator size="small" label={t('access.loadingDetails')} />}
        {error && <Notice tone="error">{t('access.detailsUnavailable')}</Notice>}
        {data && data.namesIncomplete && <Notice announce={false}>{t('access.namesIncomplete')}</Notice>}
        {data && ordered.length === 0 && <p className="access-user-note">{t('access.noDirectAssignments')}</p>}
        {data && ordered.length > 0 && <ul className="access-user-grants">
          {ordered.map(grant => {
            const role = roleById.get(grant.roleId.toLowerCase());
            return <li key={grant.grantId}>
              <div className="access-user-grant-heading">
                <strong>{role?.name ?? t('access.roleNameUnavailable')}</strong>
                <Status value={grant.isActive ? 'Active' : 'Inactive'}
                  label={t(grant.isActive ? 'access.activeGrant' : 'access.retiredGrant')} />
              </div>
              {!role && <small>{t('access.roleId', { id: grant.roleId })}</small>}
              {role && !role.isActive && <p className="access-user-note">{t('access.inactiveRole')}</p>}
              <p>{grantScopeLabel(grant.scopeKind)}</p>
              {grant.scopeKind === 'ExactAssets' && <details>
                <summary>{t('access.scopedAssets', { count: grant.assetIds.length })}</summary>
                <ul>{grant.assetIds.map(id => <li key={id}>
                  {assetById.get(id.toLowerCase())?.name ?? t('access.unnamedAsset', { id })}
                </li>)}</ul>
              </details>}
            </li>;
          })}
        </ul>}
        <p className="access-user-note">{t('access.directNotEffective')}</p>
      </section>

      <section className="access-user-detail-section" aria-label={t('access.groupAssignments')}>
        <h4>{t('access.groupAssignments')}</h4>
        <p>{t('access.notEvaluated')}</p>
        <p className="access-user-note">{t('access.groupInspectionDeferred')}</p>
      </section>
      {canManage && <div className="access-user-detail-actions">
        <ActionButton variant="primary" label={t('access.manageAssignments')}
          onClick={() => onManage(user)} />
      </div>}
    </div>
  </div>;
}
