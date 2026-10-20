import './connections.css';
import { useCallback, useEffect, useRef, useState } from 'react';
import { t } from '../localization';
import type { MessageKey } from '../localization';
import { formatTimestamp } from '../format';
import { useHashDetail } from '../routing';
import {
  ConnectionsError, connectionStatus, createConnection, deleteConnection, isDirty, loadAdapters, loadConnections,
  setConnectionEnabled, testConnection, updateConnection,
} from './connections';
import type { Adapter, Connection, ConnectionDraft, ConnectionStatus } from './connections';

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
    case 'invalid': return t('connections.errInvalid');
    case 'gatewayUnavailable': return t('connections.errGateway');
    default: return t('connections.errUncertain');
  }
}

/** Saved ways to reach outside systems. The server and the adapter decide what is valid; this only collects it. */
export function ConnectionsPage({ canManage }: { canManage: boolean }) {
  const [selected, setSelected] = useHashDetail('connections');
  const [connections, setConnections] = useState<Connection[]>([]);
  const [adapters, setAdapters] = useState<Adapter[]>([]);
  const [adaptersFailed, setAdaptersFailed] = useState(false);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const request = useRef<AbortController | null>(null);

  const reload = useCallback(async () => {
    request.current?.abort();
    const abort = new AbortController();
    request.current = abort;
    setLoading(true); setLoadError(null);
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
      if (abort.signal.aborted) return;
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
          {canManage && <button type="button" className="primary-button" disabled={adapters.length === 0}
            onClick={() => setSelected('new')}>{t('connections.new')}</button>}
        </div>
        {loading && <p className="empty-state">{t('connections.loading')}</p>}
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
                  <span className={`status-pill connection-test-${status.toLowerCase()}`}>{t(statusLabels[status])}</span>
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
  const [busy, setBusy] = useState<'save' | 'test' | 'toggle' | 'delete' | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const adapter = adapters.find(item => item.key === adapterKey) ?? null;
  const disabled = !canManage || busy !== null;
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
            <strong>{t('connections.testResult', { outcome: t(statusLabels[test.outcome]), time: formatTimestamp(test.testedAtUtc) })}</strong>
            <span>{test.message}</span>
            {test.actingAs && <span>{t('connections.testActingAs', { account: test.actingAs })}</span>}
            {!connection.lastTestIsCurrent && <span>{t('connections.testOutdated')}</span>}
          </> : <span>{t('connections.statusNotTested')}</span>}
        </div>
      )}
      {error && <p role="alert" className="status-error">{error}</p>}
      {canManage && (
        <div className="connection-actions">
          <button type="submit" className="primary-button" disabled={disabled || !adapter || !draft.name.trim() || !dirty}>
            {connection ? t('connections.save') : t('connections.create')}
          </button>
          {connection && <>
            <button type="button" className="secondary-button"
              disabled={disabled || dirty || !connection.isEnabled}
              onClick={() => void run('test', async () => onSaved(await testConnection(connection)))}>
              {busy === 'test' ? t('connections.testing') : t('connections.test')}
            </button>
            <button type="button" className="secondary-button" disabled={disabled}
              onClick={() => void run('toggle', async () => onSaved(await setConnectionEnabled(connection, !connection.isEnabled)))}>
              {connection.isEnabled ? t('connections.disable') : t('connections.enable')}
            </button>
            {confirmDelete ? <>
              <span>{t('connections.deleteConfirm', { name: connection.name })}</span>
              <button type="button" className="secondary-button" disabled={disabled}
                onClick={() => void run('delete', async () => { await deleteConnection(connection); onGone(); })}>
                {t('connections.deleteYes')}
              </button>
              <button type="button" className="secondary-button" disabled={disabled}
                onClick={() => setConfirmDelete(false)}>{t('connections.deleteNo')}</button>
            </> : <button type="button" className="secondary-button" disabled={disabled}
              onClick={() => setConfirmDelete(true)}>{t('connections.delete')}</button>}
          </>}
        </div>
      )}
      {connection && dirty && canManage && <p className="connection-help">{t('connections.testDirty')}</p>}
      {connection && !connection.isEnabled && <p className="connection-help">{t('connections.testDisabled')}</p>}
    </form>
  );
}
