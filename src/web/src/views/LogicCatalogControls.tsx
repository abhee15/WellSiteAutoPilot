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
  return (
    <div className="data-view-controls" role="group" aria-label={t('logicCatalog.controls')}>
      <div className="data-view-fields asset-view-fields">
        <label>
          <span>{t('logicCatalog.search')}</span>
          <input type="search" maxLength={120} value={criteria.search}
            placeholder={t('logicCatalog.searchPlaceholder')}
            onChange={event => onChange({ ...criteria, search: event.target.value })} />
        </label>
        <label>
          <span>{t('logicCatalog.eligibility')}</span>
          <select value={criteria.eligibility}
            onChange={event => onChange({
              ...criteria,
              eligibility: event.target.value as LogicCatalogCriteria['eligibility'],
            })}>
            <option value="all">{t('logicCatalog.all')}</option>
            <option value="enabled">{t('logicCatalog.enabled')}</option>
            <option value="disabled">{t('logicCatalog.disabled')}</option>
          </select>
        </label>
        <label>
          <span>{t('logicCatalog.runtime')}</span>
          <select value={criteria.runtime}
            onChange={event => onChange({ ...criteria, runtime: event.target.value })}>
            <option value="all">{t('logicCatalog.allRuntimes')}</option>
            {runtimes.map(runtime => (
              <option key={runtime} value={runtime}>{runtime}</option>
            ))}
          </select>
        </label>
        <label>
          <span>{t('logicCatalog.sort')}</span>
          <select value={criteria.sort}
            onChange={event => onChange({
              ...criteria,
              sort: event.target.value as LogicCatalogCriteria['sort'],
            })}>
            <option value="name">{t('logicCatalog.name')}</option>
            <option value="newest">{t('logicCatalog.newest')}</option>
            <option value="oldest">{t('logicCatalog.oldest')}</option>
          </select>
        </label>
      </div>
      <p className="data-view-note">
        {t('logicCatalog.count', { shown: matchingCount, total: totalCount })}
      </p>
    </div>
  );
}
