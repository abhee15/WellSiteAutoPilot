import { t } from '../localization';
import type { AssetInventoryCriteria } from './assetInventory';

type Props = {
  criteria: AssetInventoryCriteria;
  onChange: (next: AssetInventoryCriteria) => void;
  matchingCount: number;
  totalCount: number;
};

export function AssetInventoryControls({
  criteria, onChange, matchingCount, totalCount,
}: Props) {
  return (
    <div className="data-view-controls" role="group" aria-label={t('assets.controls')}>
      <div className="data-view-fields asset-view-fields">
        <label>
          <span>{t('assets.search')}</span>
          <input type="search" value={criteria.search} maxLength={120}
            placeholder={t('assets.searchPlaceholder')}
            onChange={event => onChange({ ...criteria, search: event.target.value })} />
        </label>
        <label>
          <span>{t('assets.activity')}</span>
          <select value={criteria.activity}
            onChange={event => onChange({
              ...criteria,
              activity: event.target.value as AssetInventoryCriteria['activity'],
            })}>
            <option value="all">{t('assets.allActivity')}</option>
            <option value="active">{t('assets.active')}</option>
            <option value="inactive">{t('assets.inactive')}</option>
          </select>
        </label>
        <label>
          <span>{t('assets.hierarchy')}</span>
          <select value={criteria.hierarchy}
            onChange={event => onChange({
              ...criteria,
              hierarchy: event.target.value as AssetInventoryCriteria['hierarchy'],
            })}>
            <option value="all">{t('assets.allHierarchy')}</option>
            <option value="roots">{t('assets.roots')}</option>
            <option value="children">{t('assets.children')}</option>
          </select>
        </label>
        <label>
          <span>{t('assets.sort')}</span>
          <select value={criteria.sort}
            onChange={event => onChange({
              ...criteria,
              sort: event.target.value as AssetInventoryCriteria['sort'],
            })}>
            <option value="name">{t('assets.name')}</option>
            <option value="newest">{t('assets.newest')}</option>
            <option value="oldest">{t('assets.oldest')}</option>
          </select>
        </label>
      </div>
      <p className="data-view-note">
        {t('assets.count', { shown: matchingCount, total: totalCount })}
      </p>
    </div>
  );
}
