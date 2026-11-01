import './connections.css';
import { ConnectionInventory } from './ConnectionInventory';
import { ActionButton } from '../components/ActionButton';
import { ConnectionImport } from './ConnectionImport';
import { ConnectionSourceChanges } from './ConnectionSourceChanges';
import { useCallback, useEffect, useRef, useState } from 'react';
import { t } from '../localization';
import type { MessageKey } from '../localization';
import { formatTimestamp } from '../format';
import { hashFor, useHashDetail } from '../routing';
import { AdaptersPanel } from './AdaptersPanel';
import {
  ConnectionsError, connectionStatus, createConnection, deleteConnection, discoverConnection, isDirty, loadAdapters, loadConnections,
  setConnectionEnabled, testConnection, updateConnection,
} from './connections';
import type { Adapter, Connection, ConnectionDraft, ConnectionStatus, Discovery, ImportSelection } from './connections';
import { LoadingIndicator } from '../components/LoadingIndicator';
import { Status } from '../components/Status';
import { useChangeRefresh } from '../live/LiveUpdates';


const statusLabels: Record<ConnectionStatus, MessageKey> = {
  disabled: 'connections.statusDisabled', notTested: 'connections.statusNotTested',
  outdated: 'connections.statusOutdated', Healthy: 'connections.outcomeHealthy',
  NeedsAttention: 'connections.outcomeNeedsAttention', Unreachable: 'connections.outcomeUnreachable',
};

function errorMessage(error: unknown): string {
  if (!(error instanceof ConnectionsError)) return t('connections.errUncertain');
  switch (error.kind) {
    case 'accessDenied': return t('connections.errDenied');
    case 'notFound': return t('connections.errNotFound');
    case 'nameTaken': return t('connections.errNameTaken');
    case 'stale': return t('connections.errStale');
    case 'disabled': return t('connections.errDisabled');
    case 'invalidSettings': return t('connections.errSettings', { detail: error.detail ?? '' });
    case 'unsupported': return t('connections.errUnsupported');
    case 'hasImports': return t('connections.errHasImports');
    case 'tooLarge': return t('connections.errTooLarge', { detail: error.detail ?? '' });
    case 'snapshotExpired': return t('connections.errSnapshotExpired');
    case 'invalid': return t('connections.errInvalid');
    case 'gatewayUnavailable': return t('connections.errGateway');
    default: return t('connections.errUncertain');
  }
}

/** Saved ways to reach outside systems. The server and the adapter decide what is valid; this only collects it. */
export function ConnectionsPage({ canManage, canAuthor = false }: { canManage: boolean; canAuthor?: boolean }) {
  const [detail, setDetail] = useHashDetail('connections');
  // `#connections/adapters` (or `adapters:<key>`) opens the Adapters tab; any other detail is a connection id or 'new'.
  const showAdapters = detail === ADAPTERS_TAB || Boolean(detail?.startsWith(ADAPTERS_TAB + ':'));
  const tabs = (
    <div className="split-tabs" role="tablist" aria-label={t('adapters.tabs')}>
      <a role="tab" href={hashFor('connections')} aria-selected={!showAdapters}>{t('adapters.tabConnections')}</a>
      <a role="tab" href={hashFor('connections', ADAPTERS_TAB)} aria-selected={showAdapters}>{t('adapters.tab')}</a>
    </div>
  );
  if (showAdapters) {
    const key = detail && detail.length > ADAPTERS_TAB.length ? detail.slice(ADAPTERS_TAB.length + 1) : null;
    return <div className="connections-page">{tabs}
      <AdaptersPanel selectedKey={key} onSelect={next => setDetail(`${ADAPTERS_TAB}:${next}`)} canAuthor={canAuthor} />
    </div>;
  }
  return <div className="connections-page">{tabs}<ConnectionsTab canManage={canManage} selected={detail} setSelected={setDetail} /></div>;
}

const ADAPTERS_TAB = 'adapters';

function ConnectionsTab({ canManage, selected, setSelected }: {
  canManage: boolean; selected: string | null; setSelected: (value: string | null) => void;
}) {
  const [connections, setConnections] = useState<Connection[]>([]);
  const [adapters, setAdapters] = useState<Adapter[]>([]);
  const [adaptersFailed, setAdaptersFailed] = useState(false);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const request = useRef<AbortController | null>(null);

  // A quiet reload (after a live signal) swaps in fresh results without showing the loading state again.
  const reload = useCallback(async (quiet = false) => {
    request.current?.abort();
    const abort = new AbortController();
    request.current = abort;
    if (!quiet) { setLoading(true); setLoadError(null); }
    try {
      const list = await loadConnections(abort.signal);
      if (request.current !== abort) return;
      setConnections(list);
      try {
        const known = await loadAdapters(abort.signal);
        if (request.current !== abort) return;
        setAdapters(known); setAdaptersFailed(false);
      } catch (caught) {
        if (abort.signal.aborted) return;
        setAdaptersFailed(true);
        if (caught instanceof ConnectionsError && caught.kind === 'accessDenied') throw caught;
      }
    } catch (caught) {
      if (abort.signal.aborted || quiet) return;
      setLoadError(caught instanceof ConnectionsError && caught.kind === 'accessDenied'
        ? t('connections.denied') : t('connections.loadFailed'));
    } finally {
      if (request.current === abort) setLoading(false);
    }
  }, []);

  useEffect(() => {
    void reload();
    return () => { request.current?.abort(); };
  }, [reload]);
  useChangeRefresh(['connections'], () => void reload(true));

  const current = selected && selected !== 'new' ? connections.find(item => item.id === selected) ?? null : null;
  const replace = (saved: Connection) => setConnections(list => {
    const rest = list.filter(item => item.id !== saved.id);
    return [...rest, saved].sort((a, b) => a.name.localeCompare(b.name));
  });

  return (
    <div className="connections-layout">
      <section className="panel" aria-label={t('connections.listLabel')}>
        <div className="panel-header">
          <h2>{t('connections.listLabel')}</h2>
          {canManage && <ActionButton label={t('connections.new')} variant="primary" disabled={adapters.length === 0} onClick={() => setSelected('new')} />}
        </div>
        {loading && <LoadingIndicator label={t('connections.loading')} />}
        {loadError && <p role="alert" className="status-error">{loadError}</p>}
        {adaptersFailed && !loadError && <p role="status" className="connection-help">{t('connections.adaptersUnavailable')}</p>}
        {!loading && !loadError && connections.length === 0 && <p className="empty-state">{t('connections.empty')}</p>}
        <ul className="connection-list">
          {connections.map(item => {
            const status = connectionStatus(item);
            return (
              <li key={item.id}>
                <button type="button" className="connection-row" aria-current={item.id === selected}
                  onClick={() => setSelected(item.id)}>
                  <strong>{item.name}</strong>
                  <small>{adapters.find(a => a.key === item.adapterKey)?.displayName ?? item.adapterKey}</small>
                  <Status value={status} label={t(statusLabels[status])} />
                </button>
              </li>
            );
          })}
        </ul>
      </section>
      <section className="panel" aria-label={t('connections.details')}>
        {selected === 'new' && canManage && adapters.length > 0 && (
          <ConnectionForm key="new" connection={null} adapters={adapters} canManage
            onSaved={saved => { replace(saved); setSelected(saved.id); }} onGone={() => undefined} />
        )}
        {current && (
          <ConnectionForm key={`${current.id}:${String(current.revision)}`} connection={current} adapters={adapters}
            canManage={canManage}
            onSaved={replace}
            onGone={() => { setSelected(null); void reload(); }} />
        )}
        {!current && selected !== 'new' && <p className="empty-state">{t('connections.pick')}</p>}
        {!canManage && <p className="connection-help">{t('connections.readOnly')}</p>}
      </section>
    </div>
  );
}

function ConnectionForm({ connection, adapters, canManage, onSaved, onGone }: {
  connection: Connection | null;
  adapters: Adapter[];
  canManage: boolean;
  onSaved: (saved: Connection) => void;
  onGone: () => void;
}) {
  const [adapterKey, setAdapterKey] = useState(connection?.adapterKey ?? '');
  const [draft, setDraft] = useState<ConnectionDraft>({
    name: connection?.name ?? '', settings: { ...(connection?.settings ?? {}) },
  });
  const [busy, setBusy] = useState<'save' | 'test' | 'toggle' | 'delete' | 'find' | null>(null);
  const [importBusy, setImportBusy] = useState(false);
  const [found, setFound] = useState<Discovery | null>(null);
  const [selection, setSelection] = useState<{ value: ImportSelection; count: number } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const adapter = adapters.find(item => item.key === adapterKey) ?? null;
  const disabled = !canManage || busy !== null || importBusy;
  const dirty = connection && adapter ? isDirty(connection, adapter, draft) : true;

  const run = async (kind: NonNullable<typeof busy>, action: () => Promise<void>) => {
    setBusy(kind); setError(null);
    try { await action(); } catch (caught) {
      setError(errorMessage(caught));
      if (caught instanceof ConnectionsError && caught.kind === 'notFound') onGone();
    } finally { setBusy(null); }
  };

  const save = () => run('save', async () => {
    if (!adapter) return;
    onSaved(connection ? await updateConnection(connection, adapter, draft) : await createConnection(adapter, draft));
  });

  const setValue = (key: string, value: string) =>
    setDraft(current => ({ ...current, settings: { ...current.settings, [key]: value } }));

  const test = connection?.lastTest ?? null;
  const status = connection ? connectionStatus(connection) : null;
  return (
    <form className="connection-form" onSubmit={event => { event.preventDefault(); void save(); }}>
      <label>
        <span>{t('connections.name')}</span>
        <input type="text" value={draft.name} maxLength={100} disabled={disabled}
          onChange={event => setDraft(current => ({ ...current, name: event.target.value }))} />
      </label>
      {connection === null ? (
        <label>
          <span>{t('connections.system')}</span>
          <select value={adapterKey} disabled={disabled} onChange={event => { setAdapterKey(event.target.value); setDraft(current => ({ ...current, settings: {} })); }}>
            <option value="">{t('connections.chooseSystem')}</option>
            {adapters.map(item => <option key={item.key} value={item.key}>{item.displayName}</option>)}
          </select>
        </label>
      ) : (
        <div>
          <strong>{t('connections.system')}</strong>: {adapter?.displayName ?? connection.adapterKey}
          <p className="connection-help">{t('connections.systemFixed')}</p>
        </div>
      )}
      {adapter && <p className="connection-help">{adapter.description}</p>}
      {adapter?.fields.map(field => {
        const label = field.required ? t('connections.requiredLabel', { label: field.label }) : field.label;
        const value = draft.settings[field.key] ?? '';
        const id = `connection-setting-${field.key}`;
        return (
          <div key={field.key} className="connection-field">
            <label htmlFor={id}>{label}</label>
            {field.kind === 'YesNo' ? (
              <select id={id} value={value} disabled={disabled} aria-describedby={`${id}-help`}
                onChange={event => setValue(field.key, event.target.value)}>
                <option value="">{t('connections.notSet')}</option>
                <option value="yes">{t('connections.yes')}</option>
                <option value="no">{t('connections.no')}</option>
              </select>
            ) : (
              <input id={id} type="text" inputMode={field.kind === 'WholeNumber' ? 'numeric' : field.kind === 'Url' ? 'url' : 'text'}
                value={value} maxLength={field.maxLength} disabled={disabled} aria-describedby={`${id}-help`}
                onChange={event => setValue(field.key, event.target.value)} />
            )}
            <span className="connection-help" id={`${id}-help`}>{field.helpText}</span>
          </div>
        );
      })}
      {connection && status && (
        <div className={`connection-test connection-test-${status.toLowerCase()}`} role="status">
          {test ? <>
            <strong>{t(test.source === 'scheduled' ? 'connections.checkResult' : 'connections.testResult', { outcome: t(statusLabels[test.outcome]), time: formatTimestamp(test.testedAtUtc) })}</strong>
            <span>{test.message}</span>
            {test.actingAs && <span>{t('connections.testActingAs', { account: test.actingAs })}</span>}
            {!connection.lastTestIsCurrent && <span>{t('connections.testOutdated')}</span>}
          </> : <span>{t('connections.statusNotTested')}</span>}
        </div>
      )}
      {error && <p role="alert" className="status-error">{error}</p>}
      {canManage && (
        <div className="connection-actions connection-actions-settings">
          <ActionButton label={connection ? t('connections.save') : t('connections.create')} variant="primary" type="submit" disabled={disabled || !adapter || !draft.name.trim() || !dirty} />
          {connection && dirty && (
            <ActionButton label={t('connections.discard')} disabled={disabled} onClick={() => setDraft({ name: connection.name, settings: { ...connection.settings } })} />
          )}
        </div>
      )}
      {canManage && connection && (
        <div className="connection-actions connection-actions-workflow" role="group" aria-label={t('connections.checkGroup')}>
          <ActionButton label={busy === 'test' ? t('connections.testing') : t('connections.test')} disabled={disabled || dirty || !connection.isEnabled} onClick={() => void run('test', async () => onSaved(await testConnection(connection)))} />
          {adapter?.canDiscover && (
            <ActionButton label={busy === 'find' ? t('connections.finding') : t('connections.find')} disabled={disabled || dirty || !connection.isEnabled} onClick={() => void run('find', async () => { setFound(null); setSelection(null); const result = await discoverConnection(connection); setFound(result); setSelection(result.complete ? { value: { snapshotId: result.snapshotId, query: '', facets: {} }, count: result.totalCount } : null); })} />
          )}
          {dirty ? <span className="connection-help">{t('connections.testDirty')}</span>
            : !connection.isEnabled && <span className="connection-help">{t('connections.testDisabled')}</span>}
        </div>
      )}
      {busy === 'find' && <p role="status">{t('connections.finding')}</p>}
      {canManage && connection && adapter?.canDiscover && !found && busy !== 'find' && <p className="connection-help">{t('inventory.discoverFirst')}</p>}
      {found && connection && <ConnectionInventory key={'inventory-' + found.snapshotId} connection={connection} found={found} stale={dirty}
        disabled={disabled} onSelect={(value, count) => setSelection(value ? { value, count } : null)} />}
      {found?.complete && found.totalCount > 0 && canManage && connection && adapter?.canDiscover && adapter.groupingFields.length > 0 && connection.isEnabled && !dirty && (
        <ConnectionImport key={'import-' + found.snapshotId} connection={connection} adapter={adapter} hierarchy={found.hierarchy}
          selection={selection?.value ?? null} selectedCount={selection?.count ?? null}
          totalCount={found?.totalCount ?? null} disabled={disabled} onBusyChange={setImportBusy} />
      )}
      {connection && adapter?.canDiscover && <ConnectionSourceChanges connectionId={connection.id} connectionRevision={connection.revision}
        canCheck={canManage && connection.isEnabled && !dirty} disabled={busy !== null || importBusy} onBusyChange={setImportBusy} />}
      {canManage && connection && (
        <section className="connection-manage" aria-label={t('connections.manageHeading')}>
          <h3>{t('connections.manageHeading')}</h3>
          <div className="connection-actions">
            <ActionButton label={connection.isEnabled ? t('connections.disable') : t('connections.enable')} disabled={disabled} onClick={() => void run('toggle', async () => onSaved(await setConnectionEnabled(connection, !connection.isEnabled)))} />
            {!confirmDelete && <ActionButton label={t('connections.delete')} disabled={disabled} onClick={() => setConfirmDelete(true)} />}
          </div>
          {confirmDelete && (
            <div className="connection-actions connection-delete-confirm" role="alertdialog" aria-label={t('connections.delete')}>
              <span>{t('connections.deleteConfirm', { name: connection.name })}</span>
              <ActionButton label={t('connections.deleteYes')} disabled={disabled} onClick={() => void run('delete', async () => { await deleteConnection(connection); onGone(); })} />
              <ActionButton label={t('connections.deleteNo')} disabled={disabled} onClick={() => setConfirmDelete(false)} />
            </div>
          )}
        </section>
      )}
    </form>
  );
}
