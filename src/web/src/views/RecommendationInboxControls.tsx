import { t } from '../localization';
import type { RecommendationInboxCriteria } from './recommendationInbox';

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
        <label>
          <span>{t('inbox.filter')}</span>
          <select value={criteria.filter}
            onChange={event => onChange({
              ...criteria,
              filter: event.target.value as RecommendationInboxCriteria['filter'],
            })}>
            <option value="all">{t('inbox.all')}</option>
            <option value="awaiting">{t('inbox.awaiting')}</option>
            <option value="expired">{t('inbox.expired')}</option>
            <option value="decided">{t('inbox.decided')}</option>
          </select>
        </label>
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
