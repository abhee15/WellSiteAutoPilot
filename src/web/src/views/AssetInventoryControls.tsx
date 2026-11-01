import { FilterToolbar, FilterField } from '../components/FilterToolbar';
import type { FilterChip } from '../components/FilterToolbar';
import { ActionButton } from '../components/ActionButton';
import { useState } from 'react';
import { t } from '../localization';
import { DEFAULT_ASSET_INVENTORY_CRITERIA } from './assetInventory';
import { MAX_SAVED_ASSET_VIEWS, loadSavedAssetViews, persistSavedAssetViews } from './assetSavedViews';
import type { SavedAssetView } from './assetSavedViews';
import type { AssetInventoryCriteria } from './assetInventory';

type Props = {
  userId: string | null | undefined;
  criteria: AssetInventoryCriteria;
  onChange: (next: AssetInventoryCriteria) => void;
  matchingCount: number;
  totalCount: number;
  /** A very large inventory is searched on the server, so only the search box is offered. */
  searchOnly?: boolean;

};

function browserStorage(): Storage | null {
  try { return typeof window === 'undefined' ? null : window.localStorage; }
  catch { return null; }
}
function newViewId(): string {
  return typeof crypto !== 'undefined' && 'randomUUID' in crypto
    ? crypto.randomUUID() : Date.now().toString(36) + '-' + Math.random().toString(36).slice(2);
}
export function AssetInventoryControls({
  userId, criteria, onChange, matchingCount, totalCount, searchOnly = false,
}: Props) {
  const [views, setViews] = useState<SavedAssetView[]>(
    () => loadSavedAssetViews(userId, browserStorage()),
  );
  const [selectedId, setSelectedId] = useState('');
  const [name, setName] = useState('');
  const [notice, setNotice] = useState<string | null>(null);

  const modify = (next: AssetInventoryCriteria) => {
    onChange(next);
    setSelectedId('');
    setNotice(null);
  };
  const select = (id: string) => {
    setSelectedId(id);
    const selected = views.find(view => view.id === id);
    if (selected) {
      onChange({ ...selected.criteria });
      setName(selected.name);
    }
    setNotice(null);
  };
  const save = () => {
    const title = name.trim();
    if (!userId) return;
    if (!title || title.length > 48) { setNotice(t('assets.saveNameInvalid')); return; }
    if (views.length >= MAX_SAVED_ASSET_VIEWS) { setNotice(t('assets.saveLimit')); return; }
    if (views.some(view => view.name.toLocaleLowerCase() === title.toLocaleLowerCase())) {
      setNotice(t('assets.saveDuplicate')); return;
    }
    const next = [...views, { id: newViewId(), name: title, criteria: { ...criteria } }];
    if (!persistSavedAssetViews(userId, next, browserStorage())) {
      setNotice(t('assets.saveFailed')); return;
    }
    setViews(next);
    setSelectedId(next[next.length - 1].id);
    setName(title);
    setNotice(t('assets.saved'));
  };
  const remove = () => {
    if (!selectedId) return;
    const next = views.filter(view => view.id !== selectedId);
    if (next.length === views.length) return;
    if (!persistSavedAssetViews(userId, next, browserStorage())) {
      setNotice(t('assets.saveFailed')); return;
    }
    setViews(next);
    setSelectedId('');
    setName('');
    setNotice(t('assets.deleted'));
  };
  const D = DEFAULT_ASSET_INVENTORY_CRITERIA;
  const chips = [
    criteria.activity !== D.activity && { id: 'activity', label: t(criteria.activity === 'active' ? 'assets.active' : 'assets.inactive'),
      onRemove: () => modify({ ...criteria, activity: D.activity }) },
    criteria.hierarchy !== D.hierarchy && { id: 'hierarchy', label: t(criteria.hierarchy === 'roots' ? 'assets.roots' : 'assets.children'),
      onRemove: () => modify({ ...criteria, hierarchy: D.hierarchy }) },
  ].filter((chip): chip is FilterChip => chip !== false);
  const isFiltered = criteria.search !== D.search || chips.length > 0 || criteria.sort !== D.sort;
  return (
    <FilterToolbar sticky label={t('assets.controls')}
      search={{ value: criteria.search, label: t('assets.search'), placeholder: t('assets.searchPlaceholder'),
        onChange: search => modify({ ...criteria, search }) }}
      chips={chips} isFiltered={isFiltered} onClear={() => modify({ ...D })}
      note={t('assets.count', { shown: matchingCount, total: totalCount })}>
        {!searchOnly && <>
      <FilterField label={t('assets.activity')}>
          <select value={criteria.activity}
            onChange={event => modify({
              ...criteria,
              activity: event.target.value as AssetInventoryCriteria['activity'],
            })}>
            <option value="all">{t('assets.allActivity')}</option>
            <option value="active">{t('assets.active')}</option>
            <option value="inactive">{t('assets.inactive')}</option>
          </select>
        </FilterField>
        <FilterField label={t('assets.hierarchy')}>
          <select value={criteria.hierarchy}
            onChange={event => modify({
              ...criteria,
              hierarchy: event.target.value as AssetInventoryCriteria['hierarchy'],
            })}>
            <option value="all">{t('assets.allHierarchy')}</option>
            <option value="roots">{t('assets.roots')}</option>
            <option value="children">{t('assets.children')}</option>
          </select>
        </FilterField>
        <FilterField label={t('assets.sort')}>
          <select value={criteria.sort}
            onChange={event => modify({
              ...criteria,
              sort: event.target.value as AssetInventoryCriteria['sort'],
            })}>
            <option value="name">{t('assets.name')}</option>
            <option value="newest">{t('assets.newest')}</option>
            <option value="oldest">{t('assets.oldest')}</option>
          </select>
        </FilterField>
      <details className="filter-toolbar-saved">
        <summary>{t('assets.savedViews')}</summary>
      <div className="data-saved-view-fields">
        <label>
          <span>{t('assets.savedViews')}</span>
          <select value={selectedId} disabled={!userId}
            onChange={event => select(event.target.value)}>
            <option value="">{t('assets.custom')}</option>
            {views.map(view => <option key={view.id} value={view.id}>{view.name}</option>)}
          </select>
        </label>
        <label>
          <span>{t('assets.viewName')}</span>
          <input value={name} maxLength={48} disabled={!userId} placeholder={t('assets.viewNamePlaceholder')}
            onChange={event => { setName(event.target.value); setNotice(null); }} />
        </label>
        <ActionButton label={t('assets.save')} disabled={!userId} onClick={save} />
        <ActionButton label={t('assets.delete')} disabled={!selectedId} onClick={remove} />
      </div>
        <p className="data-view-note">{t('assets.localNote')}</p>
        {notice && <p className="data-view-notice" role="status">{notice}</p>}
      </details>
      </>}
    </FilterToolbar>
  );
}
