import type { Section } from './NavigationLink';
import { t } from '../localization';
import { formatClock } from '../format';
import { LiveStatus } from './LiveStatus';

export function PageHeader({
  section,
  isRefreshing,
  lastUpdated,
  onRefresh,
}: {
  section: Section;
  isRefreshing: boolean;
  lastUpdated: Date | null;
  onRefresh?: () => void;
}) {
  const copy = {
    overview: {
      eyebrow: t('nav.overview'),
      title: t('overview.pageTitle'),
      description: t('overview.pageDescription'),
    },
    operations: {
      eyebrow: t('nav.operations'),
      title: t('page.operationsTitle'),
      description: t('page.operationsDescription'),
    },
    recommendations: {
      eyebrow: t('nav.recommendations'),
      title: t('inbox.pageTitle'),
      description: t('inbox.pageDescription'),
    },
    assets: {
      eyebrow: t('nav.assets'),
      title: t('page.assetsTitle'),
      description: t('page.assetsDescription'),
    },
    logic: {
      eyebrow: t('nav.logic'),
      title: t('page.logicTitle'),
      description: t('page.logicDescription'),
    },
    connections: {
      eyebrow: t('connections.eyebrow'),
      title: t('connections.pageTitle'),
      description: t('connections.pageDescription'),
    },
    status: {
      eyebrow: t('status.eyebrow'),
      title: t('status.pageTitle'),
      description: t('status.pageDescription'),
    },
    access: {
      eyebrow: t('access.eyebrow'),
      title: t('access.pageTitle'),
      description: t('access.pageDescription'),
    },
  }[section];

  return (
    <header className="page-header">
      <div>
        <p className="eyebrow">{copy.eyebrow}</p>
        <h1>{copy.title}</h1>
        <p className="page-description">{copy.description}</p>
      </div>
      <div className="header-actions">
        <LiveStatus />
        {lastUpdated && (
          <span className="last-updated">
            {t('page.updated')} {formatClock(lastUpdated)}
          </span>
        )}
        {onRefresh && (
          <button
            className="secondary-button"
            type="button"
            disabled={isRefreshing}
            onClick={onRefresh}
          >
            {isRefreshing ? t('page.refreshing') : t('page.refresh')}
          </button>
        )}
      </div>
    </header>
  );
}
