import { FilterToolbar, FilterField } from '../components/FilterToolbar';
import { DEFAULT_LOGIC_CATALOG_CRITERIA } from './logicCatalog';
import type { FilterChip } from '../components/FilterToolbar';
import { t } from '../localization';
import type { LogicCatalogCriteria } from './logicCatalog';

type Props = {
  criteria: LogicCatalogCriteria;
  onChange: (next: LogicCatalogCriteria) => void;
  runtimes: readonly string[];
  matchingCount: number;
  totalCount: number;
};

export function LogicCatalogControls({
  criteria, onChange, runtimes, matchingCount, totalCount,
}: Props) {
  const D = DEFAULT_LOGIC_CATALOG_CRITERIA;
  const chips = [
    criteria.eligibility !== D.eligibility && { id: 'eligibility', label: t(criteria.eligibility === 'enabled' ? 'logicCatalog.enabled' : 'logicCatalog.disabled'),
      onRemove: () => onChange({ ...criteria, eligibility: D.eligibility }) },
    criteria.runtime !== D.runtime && { id: 'runtime', label: criteria.runtime,
      onRemove: () => onChange({ ...criteria, runtime: D.runtime }) },
  ].filter((chip): chip is FilterChip => chip !== false);
  return (
    <FilterToolbar label={t('logicCatalog.controls')}
      search={{ value: criteria.search, label: t('logicCatalog.search'), placeholder: t('logicCatalog.searchPlaceholder'),
        onChange: search => onChange({ ...criteria, search }) }}
      chips={chips} isFiltered={criteria.search !== D.search || chips.length > 0 || criteria.sort !== D.sort}
      onClear={() => onChange({ ...D })}
      note={t('logicCatalog.count', { shown: matchingCount, total: totalCount })}>
        <FilterField label={t('logicCatalog.eligibility')}>
          <select value={criteria.eligibility}
            onChange={event => onChange({
              ...criteria,
              eligibility: event.target.value as LogicCatalogCriteria['eligibility'],
            })}>
            <option value="all">{t('logicCatalog.all')}</option>
            <option value="enabled">{t('logicCatalog.enabled')}</option>
            <option value="disabled">{t('logicCatalog.disabled')}</option>
          </select>
        </FilterField>
        <FilterField label={t('logicCatalog.runtime')}>
          <select value={criteria.runtime}
            onChange={event => onChange({ ...criteria, runtime: event.target.value })}>
            <option value="all">{t('logicCatalog.allRuntimes')}</option>
            {runtimes.map(runtime => (
              <option key={runtime} value={runtime}>{runtime}</option>
            ))}
          </select>
        </FilterField>
        <FilterField label={t('logicCatalog.sort')}>
          <select value={criteria.sort}
            onChange={event => onChange({
              ...criteria,
              sort: event.target.value as LogicCatalogCriteria['sort'],
            })}>
            <option value="name">{t('logicCatalog.name')}</option>
            <option value="newest">{t('logicCatalog.newest')}</option>
            <option value="oldest">{t('logicCatalog.oldest')}</option>
          </select>
        </FilterField>
    </FilterToolbar>
  );
}
