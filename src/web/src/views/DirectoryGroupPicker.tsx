import { SearchInput } from '../components/SearchInput';
import { useEffect, useRef, useState } from 'react';
import { t } from '../localization';
import { isDirectoryId, isGroupId } from '../security/directoryGroup';

export type DirectoryGroup = { directoryId: string; externalId: string; displayName: string; domain: string };
type Props = { disabled: boolean; onSelect: (group: DirectoryGroup | null) => void };
export function DirectoryGroupPicker({ disabled, onSelect }: Props) {
  const [selectedKey, setSelectedKey] = useState('');
  const choose = (group: DirectoryGroup | null) => {
    setSelectedKey(group ? group.directoryId + ':' + group.externalId : '');
    onSelect(group);
  };
  const selection = (items: DirectoryGroup[]) => {
    const index = items.findIndex(g => g.directoryId + ':' + g.externalId === selectedKey);
    return index < 0 ? '' : String(index);
  };
  const [supported, setSupported] = useState<boolean | null>(null);
  const [assigned, setAssigned] = useState<DirectoryGroup[]>([]);
  const [assignedError, setAssignedError] = useState(false);
  const [query, setQuery] = useState('');
  const [groups, setGroups] = useState<DirectoryGroup[]>([]);
  const [hasMore, setHasMore] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(false);
  const request = useRef<AbortController | null>(null);
  useEffect(() => {
    const abort = new AbortController();
    void fetch('/api/v1/security/directory', { credentials: 'same-origin', cache: 'no-store', signal: abort.signal })
      .then(async response => {
        if (!response.ok) throw new Error();
        const body = await response.json() as { directoryId?: unknown; supportsGroupSearch?: unknown };
        if (!isDirectoryId(body.directoryId) || typeof body.supportsGroupSearch !== 'boolean') throw new Error();
        if (!abort.signal.aborted) setSupported(body.supportsGroupSearch);
      }).catch(() => { if (!abort.signal.aborted) setError(true); });
    void fetch('/api/v1/security/groups', { credentials: 'same-origin', cache: 'no-store', signal: abort.signal })
      .then(async response => {
        if (!response.ok) throw new Error();
        const body: unknown = await response.json();
        if (!Array.isArray(body) || body.length > 500 || !body.every(g => g && isDirectoryId(g.directoryId) &&
          isGroupId(g.externalId) && typeof g.displayName === 'string' && typeof g.domain === 'string')) throw new Error();
        if (!abort.signal.aborted) setAssigned(body);
      }).catch(() => { if (!abort.signal.aborted) setAssignedError(true); });
    return () => { abort.abort(); request.current?.abort(); };
  }, []);
  const search = async () => {
    request.current?.abort();
    const abort = new AbortController(); request.current = abort;
    setBusy(true); setError(false); setGroups([]); setHasMore(false); choose(null);
    try {
      const response = await fetch('/api/v1/security/directory/groups?prefix=' + encodeURIComponent(query.trim()),
        { credentials: 'same-origin', cache: 'no-store', signal: abort.signal });
      if (!response.ok) throw new Error();
      const body = await response.json() as { groups?: unknown; hasMore?: unknown };
      if (!Array.isArray(body.groups) || body.groups.length > 50 || typeof body.hasMore !== 'boolean' ||
        !body.groups.every((g: DirectoryGroup) => g && isDirectoryId(g.directoryId) && isGroupId(g.externalId) &&
          typeof g.displayName === 'string' && g.displayName.length <= 256 && typeof g.domain === 'string')) throw new Error();
      if (!abort.signal.aborted) { setGroups(body.groups); setHasMore(body.hasMore); }
    } catch { if (!abort.signal.aborted) setError(true); }
    finally { if (request.current === abort) { request.current = null; setBusy(false); } }
  };
  return <div className="directory-group-picker">
    {assignedError && <p role="status">{t('directory.assignedFailed')}</p>}
    {assigned.length > 0 && <label>{t('directory.assigned')}<select value={selection(assigned)} disabled={disabled || busy}
      onChange={event => choose(event.target.value === '' ? null : assigned[Number(event.target.value)] ?? null)}>
      <option value="">{t('directory.select')}</option>
      {assigned.map((g, i) => <option key={g.directoryId + ':' + g.externalId} value={i}>
        {g.domain ? g.domain + '\\' : g.directoryId + ' / '}{g.displayName}
      </option>)}
    </select></label>}
    {error && <p role="alert">{t('directory.failed')}</p>}
    {supported === false && <p>{t('directory.unsupported')}</p>}
    {supported === null && !error && <p role="status">{t('access.loading')}</p>}
    {supported === true && <>
      <form className="directory-search" onSubmit={event => { event.preventDefault(); void search(); }}>
        <label htmlFor="directory-group-name">{t('directory.groupName')}</label>
        <SearchInput id="directory-group-name" value={query} maxLength={100} disabled={disabled || busy}
          controlSize="large" placeholder={t('directory.placeholder')} aria-describedby="directory-search-help"
          action={{ label: t('directory.search'), type: 'submit', busy, disabled: query.trim().length < 2 }}
          onChange={event => { setQuery(event.target.value); setGroups([]); setHasMore(false); choose(null); }} />
        <p id="directory-search-help" className="directory-picker-help">{t('directory.searchHelp')}</p>
      </form>
      <label>{t('directory.results')}<select value={selection(groups)} disabled={disabled || busy || groups.length === 0}
        key={groups.map(g => g.directoryId + ':' + g.externalId).join('|')}
        onChange={event => choose(event.target.value === '' ? null : groups[Number(event.target.value)] ?? null)}>
        <option value="">{t('directory.select')}</option>
        {groups.map((g, i) => <option key={g.directoryId + ':' + g.externalId} value={i}>
          {g.domain ? g.domain + '\\' : ''}{g.displayName}
        </option>)}
      </select></label>
      {hasMore && <p>{t('directory.refine')}</p>}
    </>}
  </div>;
}
