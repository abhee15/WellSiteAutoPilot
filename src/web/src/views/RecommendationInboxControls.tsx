import { MultiSelect } from '../components/MultiSelect';
import { t } from '../localization';
import type { MessageKey } from '../localization';
import { RECOMMENDATION_INBOX_FILTERS } from './recommendationInbox';
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
  return (
    <div className="data-view-controls" role="group"
      aria-label={t('inbox.controls')}>
      <div className="data-view-fields">
        <label>
          <span>{t('inbox.search')}</span>
          <input type="search" maxLength={120} value={criteria.search}
            placeholder={t('inbox.searchPlaceholder')}
            onChange={event => onChange({ ...criteria, search: event.target.value })} />
        </label>
        <MultiSelect
          label={t('inbox.filter')}
          allLabel={t('inbox.all')}
          options={RECOMMENDATION_INBOX_FILTERS.map(value => ({ value, label: t(filterLabels[value]) }))}
          selected={criteria.filters}
          onChange={filters => onChange({ ...criteria, filters })}
        />
        <label>
          <span>{t('inbox.sort')}</span>
          <select value={criteria.sort}
            onChange={event => onChange({
              ...criteria,
              sort: event.target.value as RecommendationInboxCriteria['sort'],
            })}>
            <option value="newest">{t('inbox.newest')}</option>
            <option value="oldest">{t('inbox.oldest')}</option>
          </select>
        </label>
      </div>
      <p className="data-view-note">
        {t('inbox.count', { shown: matchingCount, total: loadedCount })}
      </p>
    </div>
  );
}
