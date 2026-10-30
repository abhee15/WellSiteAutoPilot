import { FilterToolbar, FilterField } from '../components/FilterToolbar';
import { MultiSelect } from '../components/MultiSelect';
import { t } from '../localization';
import type { MessageKey } from '../localization';
import { DEFAULT_RECOMMENDATION_INBOX_CRITERIA, RECOMMENDATION_INBOX_FILTERS } from './recommendationInbox';
import type { RecommendationInboxCriteria, RecommendationInboxFilter } from './recommendationInbox';

const filterLabels: Record<RecommendationInboxFilter, MessageKey> = {
  awaiting: 'inbox.awaiting', expired: 'inbox.expired', decided: 'inbox.decided',
};

type Props = {
  criteria: RecommendationInboxCriteria;
  onChange: (next: RecommendationInboxCriteria) => void;
  matchingCount: number;
  loadedCount: number;
};

export function RecommendationInboxControls({
  criteria, onChange, matchingCount, loadedCount,
}: Props) {
  const isDefault = criteria.search === DEFAULT_RECOMMENDATION_INBOX_CRITERIA.search
    && criteria.sort === DEFAULT_RECOMMENDATION_INBOX_CRITERIA.sort
    && criteria.filters.length === DEFAULT_RECOMMENDATION_INBOX_CRITERIA.filters.length
    && DEFAULT_RECOMMENDATION_INBOX_CRITERIA.filters.every(value => criteria.filters.includes(value));
  const chips = criteria.filters.map(value => ({
    id: value, label: t(filterLabels[value]),
    onRemove: () => onChange({ ...criteria, filters: criteria.filters.filter(item => item !== value) }),
  }));
  return (
    <FilterToolbar label={t('inbox.controls')}
      search={{ value: criteria.search, label: t('inbox.search'), placeholder: t('inbox.searchPlaceholder'),
        onChange: search => onChange({ ...criteria, search }) }}
      chips={chips} isFiltered={!isDefault} onClear={() => onChange(DEFAULT_RECOMMENDATION_INBOX_CRITERIA)}
      note={t('inbox.count', { shown: matchingCount, total: loadedCount })}>
      <MultiSelect
        label={t('inbox.filter')}
        allLabel={t('inbox.all')}
        options={RECOMMENDATION_INBOX_FILTERS.map(value => ({ value, label: t(filterLabels[value]) }))}
        selected={criteria.filters}
        onChange={filters => onChange({ ...criteria, filters })}
      />
      <FilterField label={t('inbox.sort')}>
        <select value={criteria.sort}
          onChange={event => onChange({
            ...criteria,
            sort: event.target.value as RecommendationInboxCriteria['sort'],
          })}>
          <option value="newest">{t('inbox.newest')}</option>
          <option value="oldest">{t('inbox.oldest')}</option>
        </select>
      </FilterField>
    </FilterToolbar>
  );
}
