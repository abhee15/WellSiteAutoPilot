import { FilterToolbar, FilterField } from '../components/FilterToolbar';
import { DEFAULT_CONFIGURED_LOGIC_CRITERIA } from './configuredLogicViews';
import type { FilterChip } from '../components/FilterToolbar';
import { t } from '../localization';
import type { ConfiguredLogicCriteria } from './configuredLogicViews';

type Props = {
  criteria: ConfiguredLogicCriteria;
  onChange: (next: ConfiguredLogicCriteria) => void;
  matchingCount: number;
  totalCount: number;
};

export function ConfiguredLogicControls({
  criteria, onChange, matchingCount, totalCount,
}: Props) {
  const D = DEFAULT_CONFIGURED_LOGIC_CRITERIA;
  const statusKeys = { draft: 'configured.draft', validated: 'configured.validated', active: 'configured.statusActive', superseded: 'configured.superseded' } as const;
  const chips = [
    criteria.lifecycle !== D.lifecycle && { id: 'lifecycle', label: t(criteria.lifecycle === 'active' ? 'configured.active' : 'configured.inactive'),
      onRemove: () => onChange({ ...criteria, lifecycle: D.lifecycle }) },
    criteria.revisionStatus !== D.revisionStatus && criteria.revisionStatus !== 'all' && { id: 'revision', label: t(statusKeys[criteria.revisionStatus]),
      onRemove: () => onChange({ ...criteria, revisionStatus: D.revisionStatus }) },
  ].filter((chip): chip is FilterChip => chip !== false);
  return (
    <FilterToolbar label={t('configured.controls')}
      search={{ value: criteria.search, label: t('configured.search'), placeholder: t('configured.searchPlaceholder'),
        onChange: search => onChange({ ...criteria, search }) }}
      chips={chips} isFiltered={criteria.search !== D.search || chips.length > 0 || criteria.sort !== D.sort}
      onClear={() => onChange({ ...D })}
      note={t('configured.count', { shown: matchingCount, total: totalCount })}>
        <FilterField label={t('configured.lifecycle')}>
          <select value={criteria.lifecycle}
            onChange={event => onChange({
              ...criteria,
              lifecycle: event.target.value as ConfiguredLogicCriteria['lifecycle'],
            })}>
            <option value="all">{t('configured.all')}</option>
            <option value="active">{t('configured.active')}</option>
            <option value="inactive">{t('configured.inactive')}</option>
          </select>
        </FilterField>
        <FilterField label={t('configured.latestStatus')}>
          <select value={criteria.revisionStatus}
            onChange={event => onChange({
              ...criteria,
              revisionStatus: event.target.value as ConfiguredLogicCriteria['revisionStatus'],
            })}>
            <option value="all">{t('configured.allStatuses')}</option>
            <option value="draft">{t('configured.draft')}</option>
            <option value="validated">{t('configured.validated')}</option>
            <option value="active">{t('configured.statusActive')}</option>
            <option value="superseded">{t('configured.superseded')}</option>
          </select>
        </FilterField>
        <FilterField label={t('configured.sort')}>
          <select value={criteria.sort}
            onChange={event => onChange({
              ...criteria,
              sort: event.target.value as ConfiguredLogicCriteria['sort'],
            })}>
            <option value="name">{t('configured.name')}</option>
            <option value="newest">{t('configured.newest')}</option>
            <option value="oldest">{t('configured.oldest')}</option>
          </select>
        </FilterField>
    </FilterToolbar>
  );
}
