import { isDirectoryId, isGroupId } from '../security/directoryGroup';
import { isSecurityId as validId } from '../security/identifiers';
import type {
  AccessGrantScopeKind, AccessGrantSubjectKind, AssetDirectoryEntryResponse, CreateAccessGrantRequest,
} from '../api/generated/v1/types.gen';
import type { RoleRecord, PermissionDefinition } from './roleCatalog';
import { shortId } from '../format';

export type ScopeKind = AccessGrantScopeKind;
export type SubjectKind = AccessGrantSubjectKind;

// The compiler requires every member the server contract declares (and no
// others); the guards keep the runtime check that generated types cannot give.
const SCOPE_KINDS: Record<ScopeKind, true> = { ExactAssets: true, DeploymentWide: true, AllAssets: true };
const SUBJECT_KINDS: Record<SubjectKind, true> = { User: true, Group: true };
export function isScopeKind(value: unknown): value is ScopeKind {
  return typeof value === 'string' && Object.hasOwn(SCOPE_KINDS, value);
}
export function isSubjectKind(value: unknown): value is SubjectKind {
  return typeof value === 'string' && Object.hasOwn(SUBJECT_KINDS, value);
}
export type AssetChoice = Omit<AssetDirectoryEntryResponse, 'ownerConnectionId' | 'connectionName'> & {
  id: string; name: string; isActive: boolean;
  ownerConnectionId?: string | null; connectionName?: string | null };

/** An O(n) authorized identity index: never search the full 20,000-asset directory for each row. */
export function grantAssetLabels(available: readonly AssetChoice[]): ReadonlyMap<string, string> {
  const counts = new Map<string, number>();
  const key = (asset: AssetChoice) => JSON.stringify([asset.name, asset.ownerConnectionId ?? null]);
  for (const asset of available) counts.set(key(asset), (counts.get(key(asset)) ?? 0) + 1);
  return new Map(available.map(asset => {
    const name = asset.connectionName ? `${asset.name} (${asset.connectionName})` : asset.name;
    return [asset.id.toLowerCase(), counts.get(key(asset))! > 1 ? `${name} · ${shortId(asset.id)}` : name] as const;
  }));
}

/** Names from the protected admin directory, not from the separate assets.read snapshot. */
export function grantAssetLabel(asset: AssetChoice, available: readonly AssetChoice[]): string {
  return grantAssetLabels(available).get(asset.id.toLowerCase()) ?? asset.name;
}

export function grantAssetIdLabel(id: string, available: readonly AssetChoice[]): string {
  return grantAssetLabels(available).get(id.toLowerCase()) ?? shortId(id);
}
export type GrantDraft = CreateAccessGrantRequest & {
  subjectKind: SubjectKind; userId: string; groupId?: string; directoryId?: string; roleId: string;
  scopeKind: ScopeKind; assetIds: string[]; expectedRoleRevision: number;
};
export type GrantPreview = {
  effective: string[]; excluded: string[]; assetNames: string[];
  privileged: boolean;
};
export class GrantValidationError extends Error {}

/** Pure preview, not an authorization decision. The server is authoritative. */
export function previewGrant(draft: GrantDraft, role: RoleRecord | null,
  catalog: readonly PermissionDefinition[], assets: readonly AssetChoice[],
  allowAllAssets: boolean): GrantPreview {
  if (!(draft.subjectKind === 'User' ? validId(draft.userId) && !draft.groupId
    : draft.subjectKind === 'Group' && draft.userId === '' && isGroupId(draft.groupId) && isDirectoryId(draft.directoryId)) ||
    !validId(draft.roleId) || !role?.isActive ||
    draft.roleId.toLowerCase() !== role.roleId.toLowerCase() ||
    !Number.isSafeInteger(draft.expectedRoleRevision) || draft.expectedRoleRevision < 1 ||
    draft.expectedRoleRevision !== role.revision ||
    !isScopeKind(draft.scopeKind) ||
    (draft.scopeKind === 'AllAssets' && !allowAllAssets)) {
    throw new GrantValidationError('Invalid subject, role or scope');
  }
  const definitions = new Map(catalog.map(p => [p.key, p]));
  if (role.permissions.some(key => !definitions.has(key))) {
    throw new GrantValidationError('Unknown permission');
  }
  const available = new Map(assets.filter(a => a.isActive && validId(a.id))
    .map(asset => [asset.id.toLowerCase(), asset]));
  const selected = draft.assetIds.map(x => x.toLowerCase());
  if (draft.scopeKind === 'ExactAssets' ?
    selected.length === 0 || selected.length > 500 ||
      new Set(selected).size !== selected.length ||
      selected.some(id => !available.has(id)) :
    selected.length !== 0) {
    throw new GrantValidationError('Invalid Asset selection');
  }

  // A role can contain both Asset-scoped and deployment-wide permissions;
  // they must NEVER cross-combine merely because the role ID matches.
  const applies = draft.scopeKind === 'DeploymentWide'
    ? 'DeploymentWide' : 'ResourceScoped';
  const effective = role.permissions
    .filter(key => definitions.get(key)?.scopeKind === applies).sort();
  if (effective.length === 0) throw new GrantValidationError('No applicable permissions');
  const assetLabels = draft.scopeKind === 'ExactAssets' ? grantAssetLabels(assets) : null;
  return {
    effective,
    excluded: role.permissions.filter(key => definitions.get(key)?.scopeKind !== applies).sort(),
    assetNames: draft.scopeKind === 'ExactAssets' ?
      selected.map(id => assetLabels!.get(id) ?? available.get(id)!.name) : [],
    privileged: effective.some(key => definitions.get(key)?.isPrivileged),
  };
}
