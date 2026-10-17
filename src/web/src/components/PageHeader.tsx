import type { Section } from './NavigationLink';
import { t } from '../localization';

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
    operations: {
      eyebrow: 'Operations',
      title: 'Runtime activity',
      description: 'Recent Logic execution and runtime readiness on this Runtime Node.',
    },
    recommendations: {
      eyebrow: t('nav.recommendations'),
      title: t('inbox.pageTitle'),
      description: t('inbox.pageDescription'),
    },
    assets: {
      eyebrow: 'Assets',
      title: 'Asset inventory',
      description: 'Provider-neutral Assets and Asset Types available to configured Logic.',
    },
    logic: {
      eyebrow: 'Logic',
      title: 'Logic workspace',
      description: 'Installed Logic Modules and customer-specific configured Logic revisions.',
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
        {lastUpdated && (
          <span className="last-updated">
            Updated {lastUpdated.toLocaleTimeString()}
          </span>
        )}
        {onRefresh && (
          <button
            className="secondary-button"
            type="button"
            disabled={isRefreshing}
            onClick={onRefresh}
          >
            {isRefreshing ? 'Refreshing…' : 'Refresh'}
          </button>
        )}
      </div>
    </header>
  );
}
