import { useState } from 'react';
import { hashFor, useHashDetail } from '../routing';
import { PageTabNav } from '../components/PageTabNav';
import type { AssetResponse } from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { AccessManagementPage } from './AccessManagementPage';
import type { VerifiedSecurityUser } from './accessManagement';
import { RoleCatalogPage } from './RoleCatalogPage';
import { GrantAdministrationPage } from './GrantAdministrationPage';
import { ActivityPage } from './ActivityPage';

type Tab = 'users' | 'roles' | 'grants' | 'activity';

/** Keep Administration in the user menu, not the industrial primary navigation. */
export function AdministrationWorkspace({ currentUserId, assets = [], canReadAssets = false }: {
  currentUserId: string; assets?: readonly AssetResponse[]; canReadAssets?: boolean;
}) {
  const [detail, setDetail] = useHashDetail('access');
  // `#access/activity/<event id>` also names the selected event, so an address can point at one event.
  const [head = '', eventId = ''] = (detail ?? '').split('/');
  const tab: Tab = head === 'roles' || head === 'grants' || head === 'activity' ? head : 'users';
  const setTab = (next: Tab) => setDetail(next === 'users' ? null : next);
  const selectEvent = (id: string | null) => setDetail(id ? `activity/${id}` : 'activity');
  // The user chosen by "Assign role" on the Users tab; only the next Grants view consumes it.
  const [assignUser, setAssignUser] = useState<VerifiedSecurityUser | null>(null);
  return (
    <div>
      <PageTabNav label={t('access.pageTitle')} activeId={tab}
        tabs={[
          { id: 'users' as const, label: t('roles.usersTab'), href: hashFor('access') },
          { id: 'roles' as const, label: t('roles.tab'), href: hashFor('access', 'roles') },
          { id: 'grants' as const, label: t('grants.tab'), href: hashFor('access', 'grants') },
          { id: 'activity' as const, label: t('activity.tab'), href: hashFor('access', 'activity') },
        ]}
        onNavigate={next => { if (next === 'grants') setAssignUser(null); }} />
      <div className="administration-content">
        {tab === 'users'
          ? <AccessManagementPage currentUserId={currentUserId}
            onAssignRole={user => { setAssignUser(user); setTab('grants'); }} />
          : tab === 'roles' ? <RoleCatalogPage />
            : tab === 'activity' ? <ActivityPage selectedId={eventId || null} onSelect={selectEvent} userId={currentUserId}
              assets={assets} canReadAssets={canReadAssets} />
              : <GrantAdministrationPage currentUserId={currentUserId}
                initialUserId={assignUser?.userId} initialUser={assignUser} />}
      </div>
    </div>
  );
}
