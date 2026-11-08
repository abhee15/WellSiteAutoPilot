import { DataTable } from '../components/DataTable';
import { useCallback, useEffect, useRef, useState } from 'react';
import { RefreshCw } from 'lucide-react';
import {
  DEFAULT_USER_DIRECTORY_FILTERS, loadSecurityUserPage,
} from './accessManagement';
import type { UserDirectoryFilters, VerifiedSecurityUser } from './accessManagement';
import { t } from '../localization';
import { formatDetailTimestamp } from './executionDetail';
import { FilterToolbar, FilterSelect } from '../components/FilterToolbar';
import { Notice } from '../components/Notice';
import { LoadingIndicator, PanelLoading } from '../components/LoadingIndicator';
import { ActionButton } from '../components/ActionButton';
import { Status } from '../components/Status';
import { useSplitPaneFocus } from '../components/useSplitPaneFocus';
import { UserAccessDetailsPanel } from './UserAccessDetailsPanel';
import './splitLayout.css';
import './accessManagement.css';

interface AccessManagementPageProps {
  currentUserId: string;
  /** Opens the existing protected grants editor for this user. */
  onAssignRole?: (user: VerifiedSecurityUser) => void;
}

/** Identity directory: direct assignments are not a claim about effective/group-derived access. */
export function AccessManagementPage({ currentUserId, onAssignRole }: AccessManagementPageProps) {
  const [users, setUsers] = useState<VerifiedSecurityUser[]>([]);
  // A selected user stays inspectable when search or pagination changes. A page bound is not authorization.
  const [selected, setSelected] = useState<VerifiedSecurityUser | null>(null);
  const [filters, setFilters] = useState<UserDirectoryFilters>(DEFAULT_USER_DIRECTORY_FILTERS);
  const [searchDraft, setSearchDraft] = useState('');
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [failed, setFailed] = useState(false);
  const request = useRef<AbortController | null>(null);

  const load = useCallback(async (criteria: UserDirectoryFilters, cursor: string | null) => {
    request.current?.abort();
    const abort = new AbortController();
    request.current = abort;
    if (cursor === null) {
      setUsers([]); setNextCursor(null); setLoading(true);
    } else setLoadingMore(true);
    setFailed(false);
    try {
      const page = await loadSecurityUserPage(criteria, cursor, abort.signal);
      if (request.current !== abort || abort.signal.aborted) return;
      setUsers(current => cursor === null ? page.items : [
        ...current,
        ...page.items.filter(item => !current.some(existing => existing.userId === item.userId)),
      ]);
      // Refresh metadata when the same selected user is returned; never clear a legitimate
      // selection merely because the new page/filter no longer includes that person.
      setSelected(current => page.items.find(item => item.userId === current?.userId) ?? current);
      setNextCursor(page.nextCursor);
    } catch {
      if (request.current !== abort || abort.signal.aborted) return;
      // Do not keep protected user details visible after an uncertain or forbidden directory read.
      setUsers([]); setNextCursor(null); setSelected(null); setFailed(true);
    } finally {
      if (request.current === abort) {
        request.current = null;
        setLoading(false); setLoadingMore(false);
      }
    }
  }, []);
  useEffect(() => {
    void load(filters, null);
    return () => { request.current?.abort(); request.current = null; };
  }, [filters, currentUserId, load]);

  // Search follows typing after a short pause. All search/filter combinations run on the server.
  useEffect(() => {
    const search = searchDraft.trim();
    if (search === filters.search) return;
    const timer = window.setTimeout(() => setFilters(current => ({ ...current, search })), 350);
    return () => window.clearTimeout(timer);
  }, [searchDraft, filters.search]);

  const updateFilters = (next: Partial<UserDirectoryFilters>) =>
    setFilters(current => ({ ...current, ...next }));
  const clearFilters = () => {
    setSearchDraft('');
    setFilters(DEFAULT_USER_DIRECTORY_FILTERS);
  };
  const isFiltered = Boolean(searchDraft.trim()) || filters.accountStatus !== 'all' ||
    filters.directAssignments !== 'all';
  const chips = [
    ...(filters.search ? [{ id: 'search', label: filters.search,
      onRemove: () => { setSearchDraft(''); updateFilters({ search: '' }); } }] : []),
    ...(filters.accountStatus !== 'all' ? [{ id: 'status',
      label: t(filters.accountStatus === 'active' ? 'access.activeFilter' : 'access.inactiveFilter'),
      onRemove: () => updateFilters({ accountStatus: 'all' }) }] : []),
    ...(filters.directAssignments !== 'all' ? [{ id: 'assignments',
      label: t(filters.directAssignments === 'with' ? 'access.withAssignments' : 'access.withoutAssignments'),
      onRemove: () => updateFilters({ directAssignments: 'all' }) }] : []),
  ];
  const canAssign = (user: VerifiedSecurityUser) =>
    Boolean(onAssignRole) && user.isActive && user.userId.toLowerCase() !== currentUserId.toLowerCase();
  const splitFocus = useSplitPaneFocus(selected?.userId ?? null);
  const accessLost = useCallback(() => {
    setSelected(null);
    void load(filters, null);
  }, [filters, load]);

  return <div {...splitFocus} className="split-layout runs-layout users-directory-layout"
    data-has-selection={selected !== null}>
    <div className="split-panes">
    <section className="panel split-browse">
    <div className="panel-header"><h2>{t('access.users')}</h2>
      <ActionButton icon={RefreshCw} iconOnly label={t('access.refresh')}
        disabled={loading || loadingMore} onClick={() => void load(filters, null)} /></div>
    <FilterToolbar label={t('access.filters')}
      className="access-directory-toolbar"
      search={{ value: searchDraft, onChange: setSearchDraft, maxLength: 100,
        label: t('access.search'), placeholder: t('access.search') }}
      chips={chips} isFiltered={isFiltered} onClear={clearFilters}
      note={!loading && !failed ? t(nextCursor ? 'access.moreUsersAvailable' : 'access.showingUsers',
        { count: users.length }) : undefined}>
      <FilterSelect label={t('access.statusFilter')} value={filters.accountStatus} onChange={event =>
          updateFilters({ accountStatus: event.target.value as UserDirectoryFilters['accountStatus'] })}>
          <option value="all">{t('access.allStatuses')}</option>
          <option value="active">{t('access.activeFilter')}</option>
          <option value="inactive">{t('access.inactiveFilter')}</option>
      </FilterSelect>
      <FilterSelect label={t('access.assignmentFilter')} value={filters.directAssignments} onChange={event =>
          updateFilters({ directAssignments: event.target.value as UserDirectoryFilters['directAssignments'] })}>
          <option value="all">{t('access.allAssignments')}</option>
          <option value="with">{t('access.withAssignments')}</option>
          <option value="without">{t('access.withoutAssignments')}</option>
      </FilterSelect>
      <p className="access-filter-help">{t('access.groupNote')}</p>
    </FilterToolbar>
    {loading && <PanelLoading label={t('access.loading')} />}
    {failed && <Notice tone="error">{t('access.denied')}</Notice>}
    {!loading && !failed && users.length === 0 &&
      <Notice>{t(isFiltered ? 'access.noMatches' : 'access.noUsers')}</Notice>}
    {!loading && !failed && users.length > 0 && <DataTable label={t('access.tableLabel')}>
        <thead><tr><th>{t('access.identity')}</th><th>{t('access.directAssignments')}</th>
          <th>{t('session.account')}</th><th className="user-col-secondary">{t('access.lastSeen')}</th>
          <th className="user-col-actions">{t('access.actions')}</th></tr></thead>
        <tbody>{users.map(user => <tr key={user.userId} data-selected={selected?.userId === user.userId}
          onClick={event => {
            if ((event.target as HTMLElement).closest('button, a')) return;
            setSelected(user);
          }}>
          <td>
            <button type="button" className="asset-select access-user-open"
              data-split-focus-key={user.userId}
              aria-current={selected?.userId === user.userId ? 'true' : undefined}
              aria-label={t('access.inspectUser', { name: user.displayName || user.identityName })}
              onClick={() => setSelected(current => current?.userId === user.userId ? null : user)}>
              <strong>{user.displayName || user.identityName}</strong>
            </button>
            {user.displayName && <small>{user.identityName}</small>}
          </td>
          <td>{user.activeDirectGrantCount > 0 ? user.activeDirectGrantCount : t('access.noDirectRole')}</td>
          <td><Status value={user.isActive ? 'Active' : 'Inactive'}
            label={user.isActive ? t('session.active') : t('session.inactive')} /></td>
          <td className="user-col-secondary">{formatDetailTimestamp(user.lastSeenAtUtc)}</td>
          <td className="user-col-actions">{canAssign(user) && <ActionButton label={t('access.assignRole')}
            aria-label={t('access.assignRoleTo', { name: user.displayName || user.identityName })}
            onClick={() => onAssignRole?.(user)} />}</td>
        </tr>)}</tbody>
      </DataTable>}
    {!loading && !failed && nextCursor &&
      <div className="access-directory-more"><ActionButton label={t('access.loadMore')}
        disabled={loadingMore} onClick={() => void load(filters, nextCursor)} />
        {loadingMore && <LoadingIndicator label={t('access.loading')} size="small" inline />}
      </div>}
    </section>
    <section className="split-selected" tabIndex={-1} aria-label={t('access.detailHeading')}>
      <ActionButton label={t('access.backToList')} className="split-back"
        onClick={() => setSelected(null)} />
      {selected && <UserAccessDetailsPanel key={selected.userId} user={selected}
        canManage={canAssign(selected)} onManage={user => onAssignRole?.(user)}
        onClose={() => setSelected(null)} onAccessDenied={accessLost} />}
    </section>
    </div>
  </div>;
}
