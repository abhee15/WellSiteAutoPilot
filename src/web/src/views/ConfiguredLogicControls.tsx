import { t } from '../localization';
import type { ConfiguredLogicCriteria } from './configuredLogicViews';

type Props = {
  criteria: ConfiguredLogicCriteria;
  onChange: (next: ConfiguredLogicCriteria) => void;
  matchingCount: number;
  totalCount: number;
  /** The search box is shown above by the page, as in Assets; only the other filters are drawn here. */
  hideSearch?: boolean;
};

export function ConfiguredLogicControls({
  criteria, onChange, matchingCount, totalCount, hideSearch = false,
}: Props) {
  return (
    <div className="data-view-controls" role="group"
      aria-label={t('configured.controls')}>
      <div className="data-view-fields asset-view-fields">
        {!hideSearch && <label>
          <span>{t('configured.search')}</span>
          <input type="search" maxLength={120} value={criteria.search}
            placeholder={t('configured.searchPlaceholder')}
            onChange={event => onChange({ ...criteria, search: event.target.value })} />
        </label>}
        <label>
          <span>{t('configured.lifecycle')}</span>
          <select value={criteria.lifecycle}
            onChange={event => onChange({
              ...criteria,
              lifecycle: event.target.value as ConfiguredLogicCriteria['lifecycle'],
            })}>
            <option value="all">{t('configured.all')}</option>
            <option value="active">{t('configured.active')}</option>
            <option value="inactive">{t('configured.inactive')}</option>
          </select>
        </label>
        <label>
          <span>{t('configured.latestStatus')}</span>
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
        </label>
        <label>
          <span>{t('configured.sort')}</span>
          <select value={criteria.sort}
            onChange={event => onChange({
              ...criteria,
              sort: event.target.value as ConfiguredLogicCriteria['sort'],
            })}>
            <option value="name">{t('configured.name')}</option>
            <option value="newest">{t('configured.newest')}</option>
            <option value="oldest">{t('configured.oldest')}</option>
          </select>
        </label>
      </div>
      <p className="data-view-note">
        {t('configured.count', { shown: matchingCount, total: totalCount })}
      </p>
    </div>
  );
}
