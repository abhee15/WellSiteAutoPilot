import { useState } from 'react';
import { useHashDetail } from '../routing';
import { t } from '../localization';
import { AccessManagementPage } from './AccessManagementPage';
import { RoleCatalogPage } from './RoleCatalogPage';
import { GrantAdministrationPage } from './GrantAdministrationPage';

type Tab = 'users' | 'roles' | 'grants';

/** Keep Administration in the user menu, not the industrial primary navigation. */
export function AdministrationWorkspace({ currentUserId }: { currentUserId: string }) {
  const [detail, setDetail] = useHashDetail('access');
  const tab: Tab = detail === 'roles' || detail === 'grants' ? detail : 'users';
  const setTab = (next: Tab) => setDetail(next === 'users' ? null : next);
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
      </div>
      <div role="tabpanel" id={tab === 'users' ? 'admin-users' : tab === 'roles' ? 'admin-roles' : 'admin-grants'}
        aria-labelledby={tab === 'users' ? 'admin-users-tab' : tab === 'roles' ?
          'admin-roles-tab' : 'admin-grants-tab'}>
        {tab === 'users'
          ? <AccessManagementPage currentUserId={currentUserId}
            onAssignRole={userId => { setAssignUserId(userId); setTab('grants'); }} />
          : tab === 'roles' ? <RoleCatalogPage />
            : <GrantAdministrationPage currentUserId={currentUserId} initialUserId={assignUserId} />}
      </div>
    </div>
  );
}
