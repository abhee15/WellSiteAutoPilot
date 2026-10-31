import { ActionButton } from '../components/ActionButton';
import { FilterToolbar, FilterField } from '../components/FilterToolbar';
import { useEffect, useState } from 'react';
import { MultiSelect } from '../components/MultiSelect';
import { t } from '../localization';
import type { MessageKey } from '../localization';
import type { OperationsViewCriteria, SavedOperationsView } from './operationsViews';
import {
  DEFAULT_OPERATIONS_VIEW,
  OPERATIONS_STATUS_FILTERS,
  MAX_SAVED_OPERATIONS_VIEWS,
  loadSavedOperationsViews,
  persistSavedOperationsViews,
  sameCriteria,
} from './operationsViews';

const statusLabels: Record<(typeof OPERATIONS_STATUS_FILTERS)[number], MessageKey> = {
  active: 'views.active', attention: 'views.attention', completed: 'views.completed',
};

type Props = {
  userId: string | null | undefined;
  criteria: OperationsViewCriteria;
  onChange: (next: OperationsViewCriteria) => void;
  matchingCount: number;
  recentCount: number;
};

function browserStorage(): Storage | null {
  try {
    return typeof window === 'undefined' ? null : window.localStorage;
  } catch {
    return null;
  }
}

function newViewId(): string {
  return typeof crypto !== 'undefined' && 'randomUUID' in crypto
    ? crypto.randomUUID()
    : Date.now().toString(36) + '-' + Math.random().toString(36).slice(2);
}

export function OperationsViewControls({
  userId,
  criteria,
  onChange,
  matchingCount,
  recentCount,
}: Props) {
  const [savedViews, setSavedViews] = useState<SavedOperationsView[]>(
    () => loadSavedOperationsViews(userId, browserStorage()),
  );
  const [selectedId, setSelectedId] = useState('');
  const [name, setName] = useState('');
  const [notice, setNotice] = useState<string | null>(null);

  // The list can also be changed from outside (a summary card, "Clear filters"); then the chosen saved view no longer
  // describes what is shown, so it must not stay selected.
  useEffect(() => {
    if (!selectedId) return;
    const selected = savedViews.find(view => view.id === selectedId);
    if (!selected || !sameCriteria(selected.criteria, criteria)) setSelectedId('');
  }, [criteria, selectedId, savedViews]);

  const changeCriteria = (next: OperationsViewCriteria) => {
    onChange(next);
    setSelectedId('');
    setNotice(null);
  };

  const selectView = (viewId: string) => {
    setSelectedId(viewId);
    const selected = savedViews.find(view => view.id === viewId);
    if (selected) {
      onChange({ ...selected.criteria });
      setName(selected.name);
    }
    setNotice(null);
  };

  const saveView = () => {
    const trimmedName = name.trim();
    if (!userId) {
      setNotice(t('views.signIn'));
      return;
    }
    if (!trimmedName || trimmedName.length > 48) {
      setNotice(t('views.nameInvalid'));
      return;
    }
    if (savedViews.length >= MAX_SAVED_OPERATIONS_VIEWS) {
      setNotice(t('views.limitReached'));
      return;
    }
    if (savedViews.some(view => view.name.toLowerCase() === trimmedName.toLowerCase())) {
      setNotice(t('views.nameExists'));
      return;
    }
    const view = { id: newViewId(), name: trimmedName, criteria: { ...criteria } };
    const updated = [...savedViews, view];
    if (!persistSavedOperationsViews(userId, updated, browserStorage())) {
      setNotice(t('views.saveFailed'));
      return;
    }
    setSavedViews(updated);
    setSelectedId(view.id);
    setName(trimmedName);
    setNotice(t('views.saved'));
  };

  const deleteView = () => {
    const updated = savedViews.filter(view => view.id !== selectedId);
    if (!selectedId || updated.length === savedViews.length) return;
    if (!persistSavedOperationsViews(userId, updated, browserStorage())) {
      setNotice(t('views.saveFailed'));
      return;
    }
    setSavedViews(updated);
    setSelectedId('');
    setName('');
    setNotice(t('views.deleted'));
  };

  const chips = criteria.statuses.map(value => ({
    id: value, label: t(statusLabels[value]),
    onRemove: () => changeCriteria({ ...criteria, statuses: criteria.statuses.filter(item => item !== value) }),
  }));
  return (
    <FilterToolbar label={t('views.controls')}
      search={{ value: criteria.search, label: t('views.search'), placeholder: t('views.searchPlaceholder'),
        onChange: search => changeCriteria({ ...criteria, search }) }}
      chips={chips} isFiltered={!sameCriteria(criteria, DEFAULT_OPERATIONS_VIEW)}
      onClear={() => changeCriteria({ ...DEFAULT_OPERATIONS_VIEW })}
      note={<>{t('views.count', { shown: matchingCount, total: recentCount })}</>}>
      <MultiSelect
        label={t('views.status')}
        allLabel={t('views.all')}
        options={OPERATIONS_STATUS_FILTERS.map(value => ({ value, label: t(statusLabels[value]) }))}
        selected={criteria.statuses}
        onChange={statuses => changeCriteria({ ...criteria, statuses })}
      />
      <FilterField label={t('views.sort')}>
        <select
          value={criteria.sort}
          onChange={event => changeCriteria({
            ...criteria,
            sort: event.target.value as OperationsViewCriteria['sort'],
          })}
        >
          <option value="newest">{t('views.newest')}</option>
          <option value="oldest">{t('views.oldest')}</option>
        </select>
      </FilterField>
      <details className="filter-toolbar-saved">
        <summary>{t('views.savedViews')}</summary>
      <div className="data-saved-view-fields">
        <label>
          <span>{t('views.savedViews')}</span>
          <select
            value={selectedId}
            disabled={!userId}
            onChange={event => selectView(event.target.value)}
          >
            <option value="">{t('views.custom')}</option>
            {savedViews.map(view => (
              <option key={view.id} value={view.id}>{view.name}</option>
            ))}
          </select>
        </label>
        <label>
          <span>{t('views.name')}</span>
          <input
            value={name}
            maxLength={48}
            disabled={!userId}
            placeholder={t('views.namePlaceholder')}
            onChange={event => {
              setName(event.target.value);
              setNotice(null);
            }}
          />
        </label>
        <ActionButton label={t('views.save')} onClick={saveView} disabled={!userId} />
        <ActionButton label={t('views.delete')} onClick={deleteView} disabled={!selectedId} />
      </div>
        <p className="data-view-note">{t('views.deviceNote')}</p>
        {notice && <p className="data-view-notice" role="status">{notice}</p>}
      </details>
    </FilterToolbar>
  );
}
