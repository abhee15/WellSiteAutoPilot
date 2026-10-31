import { useState } from 'react';
import { useHashDetail } from '../routing';
import { t } from '../localization';
import { AccessManagementPage } from './AccessManagementPage';
import { RoleCatalogPage } from './RoleCatalogPage';
import { GrantAdministrationPage } from './GrantAdministrationPage';
import { ActivityPage } from './ActivityPage';

type Tab = 'users' | 'roles' | 'grants' | 'activity';

/** Keep Administration in the user menu, not the industrial primary navigation. */
export function AdministrationWorkspace({ currentUserId }: { currentUserId: string }) {
  const [detail, setDetail] = useHashDetail('access');
  // `#access/activity/<event id>` also names the selected event, so an address can point at one event.
  const [head = '', eventId = ''] = (detail ?? '').split('/');
  const tab: Tab = head === 'roles' || head === 'grants' || head === 'activity' ? head : 'users';
  const setTab = (next: Tab) => setDetail(next === 'users' ? null : next);
  const selectEvent = (id: string | null) => setDetail(id ? `activity/${id}` : 'activity');
  // The user chosen by "Assign role" on the Users tab; only the next Grants view consumes it.
  const [assignUserId, setAssignUserId] = useState('');
  return (
    <div>
      <div className="administration-tabs" role="tablist" aria-label={t('access.pageTitle')}>
        <button type="button" role="tab" className="secondary-button"
          aria-selected={tab === 'users'} id="admin-users-tab" aria-controls="admin-users"
          onClick={() => setTab('users')}>{t('roles.usersTab')}</button>
        <button type="button" role="tab" className="secondary-button"
          aria-selected={tab === 'roles'} id="admin-roles-tab" aria-controls="admin-roles"
          onClick={() => setTab('roles')}>{t('roles.tab')}</button>
        <button type="button" role="tab" className="secondary-button"
          aria-selected={tab === 'grants'} id="admin-grants-tab" aria-controls="admin-grants"
          onClick={() => { setAssignUserId(''); setTab('grants'); }}>{t('grants.tab')}</button>
        <button type="button" role="tab" className="secondary-button"
          aria-selected={tab === 'activity'} id="admin-activity-tab" aria-controls="admin-activity"
          onClick={() => setTab('activity')}>{t('activity.tab')}</button>
      </div>
      <div role="tabpanel" id={`admin-${tab}`} aria-labelledby={`admin-${tab}-tab`}>
        {tab === 'users'
          ? <AccessManagementPage currentUserId={currentUserId}
            onAssignRole={userId => { setAssignUserId(userId); setTab('grants'); }} />
          : tab === 'roles' ? <RoleCatalogPage />
            : tab === 'activity' ? <ActivityPage selectedId={eventId || null} onSelect={selectEvent} />
              : <GrantAdministrationPage currentUserId={currentUserId} initialUserId={assignUserId} />}
      </div>
    </div>
  );
}
