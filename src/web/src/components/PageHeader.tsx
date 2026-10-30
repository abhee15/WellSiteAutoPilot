import type { Section } from './NavigationLink';
import { t } from '../localization';
import { formatClock } from '../format';
import { LiveStatus } from './LiveStatus';
import { ActionButton } from './ActionButton';
import { RefreshCw } from 'lucide-react';

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
      title: t('overview.pageTitle'),
    },
    operations: {
      title: t('page.operationsTitle'),
    },
    recommendations: {
      title: t('inbox.pageTitle'),
    },
    assets: {
      title: t('page.assetsTitle'),
    },
    logic: {
      title: t('page.logicTitle'),
    },
    connections: {
      title: t('connections.pageTitle'),
    },
    status: {
      title: t('status.pageTitle'),
    },
    access: {
      title: t('access.pageTitle'),
    },
  }[section];

  return (
    <header className="page-header">
      <h1>{copy.title}</h1>
      <div className="header-actions">
        <LiveStatus />
        {lastUpdated && (
          <span className="last-updated">
            {t('page.updated')} {formatClock(lastUpdated)}
          </span>
        )}
        {onRefresh && (
          <ActionButton icon={RefreshCw} iconOnly busy={isRefreshing}
            label={isRefreshing ? t('page.refreshing') : t('page.refresh')} onClick={onRefresh} />
        )}
      </div>
    </header>
  );
}
