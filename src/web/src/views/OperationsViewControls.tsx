import './operatorItems.css';
import { ActionButton } from '../components/ActionButton';
import { FilterToolbar, FilterSelect } from '../components/FilterToolbar';
import { SelectField, TextField } from '../components/FormFields';
import { Notice } from '../components/Notice';
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
  showsOnly,
  toggleOnly,
} from './operationsViews';

const statusLabels: Record<(typeof OPERATIONS_STATUS_FILTERS)[number], MessageKey> = {
  active: 'views.active', attention: 'operations.runIssues', completed: 'views.completed',
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
  const [notice, setNotice] = useState<{ tone: 'success' | 'warning'; text: string } | null>(null);

  // The list can also be changed from outside (for example, "Clear filters"); then the chosen saved view no longer
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
      setNotice({ tone: 'warning', text: t('views.signIn') });
      return;
    }
    if (!trimmedName || trimmedName.length > 48) {
      setNotice({ tone: 'warning', text: t('views.nameInvalid') });
      return;
    }
    if (savedViews.length >= MAX_SAVED_OPERATIONS_VIEWS) {
      setNotice({ tone: 'warning', text: t('views.limitReached') });
      return;
    }
    if (savedViews.some(view => view.name.toLowerCase() === trimmedName.toLowerCase())) {
      setNotice({ tone: 'warning', text: t('views.nameExists') });
      return;
    }
    const view = { id: newViewId(), name: trimmedName, criteria: { ...criteria } };
    const updated = [...savedViews, view];
    if (!persistSavedOperationsViews(userId, updated, browserStorage())) {
      setNotice({ tone: 'warning', text: t('views.saveFailed') });
      return;
    }
    setSavedViews(updated);
    setSelectedId(view.id);
    setName(trimmedName);
    setNotice({ tone: 'success', text: t('views.saved') });
  };

  const deleteView = () => {
    const updated = savedViews.filter(view => view.id !== selectedId);
    if (!selectedId || updated.length === savedViews.length) return;
    if (!persistSavedOperationsViews(userId, updated, browserStorage())) {
      setNotice({ tone: 'warning', text: t('views.saveFailed') });
      return;
    }
    setSavedViews(updated);
    setSelectedId('');
    setName('');
    setNotice({ tone: 'success', text: t('views.deleted') });
  };

  const chips = criteria.statuses.map(value => ({
    id: value, label: t(statusLabels[value]),
    onRemove: () => changeCriteria({ ...criteria, statuses: criteria.statuses.filter(item => item !== value) }),
  }));
  return (
    <FilterToolbar label={t('views.controls')}
      search={{ value: criteria.search, label: t('views.search'), placeholder: t('views.searchPlaceholder'),
        onChange: search => changeCriteria({ ...criteria, search }) }}
      trailing={<div className="run-quick-filters" role="group" aria-label={t('views.status')}>
        {(['active', 'attention'] as const).map(status => (
          <ActionButton className="run-quick-filter" aria-pressed={showsOnly(criteria, status)} key={status}
            onClick={() => changeCriteria(toggleOnly(criteria, status))}
            label={t(statusLabels[status])} />
        ))}
      </div>}
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
      <FilterSelect label={t('views.sort')} value={criteria.sort}
        onChange={event => changeCriteria({
          ...criteria,
          sort: event.target.value as OperationsViewCriteria['sort'],
        })}>
        <option value="newest">{t('views.newest')}</option>
        <option value="oldest">{t('views.oldest')}</option>
      </FilterSelect>
      <details className="filter-toolbar-saved">
        <summary>{t('views.savedViews')}</summary>
      <div className="data-saved-view-fields">
        <SelectField label={t('views.savedViews')} value={selectedId} disabled={!userId}
          onChange={event => selectView(event.target.value)}>
          <option value="">{t('views.custom')}</option>
          {savedViews.map(view => (
            <option key={view.id} value={view.id}>{view.name}</option>
          ))}
        </SelectField>
        <TextField label={t('views.name')} value={name} maxLength={48} disabled={!userId} placeholder={t('views.namePlaceholder')}
          onChange={event => {
            setName(event.target.value);
            setNotice(null);
          }} />
        <ActionButton label={t('views.save')} onClick={saveView} disabled={!userId} />
        <ActionButton label={t('views.delete')} onClick={deleteView} disabled={!selectedId} />
      </div>
        <p className="data-view-note">{t('views.deviceNote')}</p>
        {notice && <Notice tone={notice.tone}>{notice.text}</Notice>}
      </details>
    </FilterToolbar>
  );
}
