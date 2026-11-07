import { FilterToolbar, FilterSelect } from '../components/FilterToolbar';
import type { FilterChip } from '../components/FilterToolbar';
import { ActionButton } from '../components/ActionButton';
import { SelectField, TextField } from '../components/FormFields';
import { Notice } from '../components/Notice';
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
  const [notice, setNotice] = useState<{ tone: 'success' | 'warning'; text: string } | null>(null);
  const warn = (key: Parameters<typeof t>[0]) => setNotice({ tone: 'warning', text: t(key) });

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
    if (!title || title.length > 48) { warn('assets.saveNameInvalid'); return; }
    if (views.length >= MAX_SAVED_ASSET_VIEWS) { warn('assets.saveLimit'); return; }
    if (views.some(view => view.name.toLocaleLowerCase() === title.toLocaleLowerCase())) {
      warn('assets.saveDuplicate'); return;
    }
    const next = [...views, { id: newViewId(), name: title, criteria: { ...criteria } }];
    if (!persistSavedAssetViews(userId, next, browserStorage())) {
      warn('assets.saveFailed'); return;
    }
    setViews(next);
    setSelectedId(next[next.length - 1].id);
    setName(title);
    setNotice({ tone: 'success', text: t('assets.saved') });
  };
  const remove = () => {
    if (!selectedId) return;
    const next = views.filter(view => view.id !== selectedId);
    if (next.length === views.length) return;
    if (!persistSavedAssetViews(userId, next, browserStorage())) {
      warn('assets.saveFailed'); return;
    }
    setViews(next);
    setSelectedId('');
    setName('');
    setNotice({ tone: 'success', text: t('assets.deleted') });
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
      <FilterSelect label={t('assets.activity')} value={criteria.activity}
            onChange={event => modify({
              ...criteria,
              activity: event.target.value as AssetInventoryCriteria['activity'],
            })}>
            <option value="all">{t('assets.allActivity')}</option>
            <option value="active">{t('assets.active')}</option>
            <option value="inactive">{t('assets.inactive')}</option>
          </FilterSelect>
        <FilterSelect label={t('assets.hierarchy')} value={criteria.hierarchy}
            onChange={event => modify({
              ...criteria,
              hierarchy: event.target.value as AssetInventoryCriteria['hierarchy'],
            })}>
            <option value="all">{t('assets.allHierarchy')}</option>
            <option value="roots">{t('assets.roots')}</option>
            <option value="children">{t('assets.children')}</option>
          </FilterSelect>
        <FilterSelect label={t('assets.sort')} value={criteria.sort}
            onChange={event => modify({
              ...criteria,
              sort: event.target.value as AssetInventoryCriteria['sort'],
            })}>
            <option value="name">{t('assets.name')}</option>
            <option value="newest">{t('assets.newest')}</option>
            <option value="oldest">{t('assets.oldest')}</option>
          </FilterSelect>
      <details className="filter-toolbar-saved">
        <summary>{t('assets.savedViews')}</summary>
      <div className="data-saved-view-fields">
        <SelectField label={t('assets.savedViews')} value={selectedId} disabled={!userId}
          onChange={event => select(event.target.value)}>
          <option value="">{t('assets.custom')}</option>
          {views.map(view => <option key={view.id} value={view.id}>{view.name}</option>)}
        </SelectField>
        <TextField label={t('assets.viewName')} value={name} maxLength={48} disabled={!userId} placeholder={t('assets.viewNamePlaceholder')}
          onChange={event => { setName(event.target.value); setNotice(null); }} />
        <ActionButton label={t('assets.save')} disabled={!userId} onClick={save} />
        <ActionButton label={t('assets.delete')} disabled={!selectedId} onClick={remove} />
      </div>
        <p className="data-view-note">{t('assets.localNote')}</p>
        {notice && <Notice tone={notice.tone}>{notice.text}</Notice>}
      </details>
      </>}
    </FilterToolbar>
  );
}
