import type { AssetDirectoryEntryResponse, CreateUserAccessGrantRequest } from '../api/generated/v1/types.gen';
import type { RoleRecord, PermissionDefinition } from './roleCatalog';

export type ScopeKind = 'ExactAssets' | 'DeploymentWide' | 'AllAssets';
export type AssetChoice = AssetDirectoryEntryResponse & { id: string; name: string; isActive: boolean };
export type GrantDraft = CreateUserAccessGrantRequest & {
  subjectKind: 'User'; userId: string; roleId: string;
  scopeKind: ScopeKind; assetIds: string[]; expectedRoleRevision: number;
};
export type GrantPreview = {
  effective: string[]; excluded: string[]; assetNames: string[];
  privileged: boolean;
};
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
export class GrantValidationError extends Error {}

/** Pure preview, not an authorization decision. The server is authoritative. */
export function previewGrant(draft: GrantDraft, role: RoleRecord | null,
  catalog: readonly PermissionDefinition[], assets: readonly AssetChoice[],
  allowAllAssets: boolean): GrantPreview {
  if (draft.subjectKind !== 'User' || !uuid.test(draft.userId) ||
    !uuid.test(draft.roleId) || !role?.isActive ||
    draft.roleId.toLowerCase() !== role.roleId.toLowerCase() ||
    !Number.isSafeInteger(draft.expectedRoleRevision) || draft.expectedRoleRevision < 1 ||
    draft.expectedRoleRevision !== role.revision ||
    !(['ExactAssets', 'DeploymentWide', 'AllAssets'] as const).includes(draft.scopeKind) ||
    (draft.scopeKind === 'AllAssets' && !allowAllAssets)) {
    throw new GrantValidationError('Invalid subject, role or scope');
  }
  const definitions = new Map(catalog.map(p => [p.key, p]));
  if (role.permissions.some(key => !definitions.has(key))) {
    throw new GrantValidationError('Unknown permission');
  }
  const available = new Map(assets.filter(a => a.isActive && uuid.test(a.id))
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
  return {
    effective,
    excluded: role.permissions.filter(key => definitions.get(key)?.scopeKind !== applies).sort(),
    assetNames: draft.scopeKind === 'ExactAssets' ?
      selected.map(id => available.get(id)!.name) : [],
    privileged: effective.some(key => definitions.get(key)?.isPrivileged),
  };
}
