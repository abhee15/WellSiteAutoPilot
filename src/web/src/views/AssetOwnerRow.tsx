import { hashFor } from '../routing';
import { t } from '../localization';
import type { OwnerGroup } from './assetHierarchy';

/** The name shown for a root: the Connection's name, or Standalone for assets no Connection owns. */
export function ownerLabel(owner: OwnerGroup): string {
  return owner.connectionId === null ? t('assetOwner.standalone') : owner.name ?? t('assetOwner.unknown');
}

/** The key that identifies a root in expansion state. */
export const ownerKey = (connectionId: string | null | undefined): string => 'owner:' + (connectionId ?? 'standalone');

/**
 * One Connection (or Standalone) root of the hierarchy. It is not an asset: it groups the assets a
 * Connection owns, and links to the Connection that produced them.
 */
export function AssetOwnerRow({
  owner, expanded, onToggle, canOpenConnection,
}: {
  owner: OwnerGroup;
  expanded: boolean;
  onToggle: () => void;
  canOpenConnection: boolean;
}) {
  const label = ownerLabel(owner);
  return (
    <div className="asset-tree-row asset-owner-row">
      <button type="button" className="asset-tree-toggle"
        aria-label={t(expanded ? 'assetTree.collapse' : 'assetTree.expand', { name: label })}
        onClick={onToggle}>{expanded ? '−' : '+'}</button>
      <button type="button" className="asset-select asset-tree-name" onClick={onToggle}
        aria-label={t('assetTree.open', { name: label })}>
        <span className="asset-owner-icon" aria-hidden="true">{owner.connectionId === null ? '▭' : '⏚'}</span>
        {' '}{label}
      </button>
      <small>{t('assetOwner.count', { count: String(owner.assetCount) })}</small>
      {owner.connectionId !== null && !owner.isEnabled && <small>{t('assetOwner.disabled')}</small>}
      {owner.connectionId !== null && canOpenConnection &&
        <a className="asset-owner-link" href={hashFor('connections', owner.connectionId)}>{t('assetOwner.open')}</a>}
    </div>
  );
}
