import { useEffect, useState } from 'react';
import type { AdapterOperationsResponse, ConnectionOperationsResponse } from '../api/generated/v1/types.gen';
import { ActionButton } from '../components/ActionButton';
import { PanelHeader } from '../components/common';
import { PanelLoading } from '../components/LoadingIndicator';
import { SelectField } from '../components/FormFields';
import { Notice } from '../components/Notice';
import { SearchInput } from '../components/SearchInput';
import { useSplitPaneFocus } from '../components/useSplitPaneFocus';
import { AdapterOperationDetails } from './AdapterOperationDetails';
import { loadAdapterOperations, loadConnectionOperations } from './adapterOperations';
import { ConnectionsError, loadConnections } from './connections';
import type { Connection } from './connections';
import './adapterOperations.css';

/** Provider-neutral metadata explorer. No preview or command execution is issued here. */
export function AdaptersPanel() {
  const [profiles, setProfiles] = useState<AdapterOperationsResponse[]>([]);
  const [connections, setConnections] = useState<Connection[]>([]);
  const [adapterKey, setAdapterKey] = useState('');
  const [query, setQuery] = useState('');
  const [selection, setSelection] = useState<string | null>(null);
  const [connectionId, setConnectionId] = useState('');
  const [availability, setAvailability] = useState<ConnectionOperationsResponse | null>(null);
  const [availabilityError, setAvailabilityError] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  const splitFocus = useSplitPaneFocus(selection);

  useEffect(() => {
    const abort = new AbortController();
    setLoading(true); setError(null); setProfiles([]); setConnections([]); setSelection(null);
    void Promise.all([loadAdapterOperations(abort.signal), loadConnections(abort.signal)]).then(([adapters, saved]) => {
      if (abort.signal.aborted) return;
      setProfiles(adapters); setConnections(saved); setAdapterKey(adapters[0]?.adapterKey ?? '');
    }).catch((caught: unknown) => {
      if (!abort.signal.aborted) setError(caught instanceof ConnectionsError && caught.kind === 'accessDenied'
        ? 'You do not have access to connection metadata.' : 'Adapter operations could not be loaded. Try again.');
    }).finally(() => { if (!abort.signal.aborted) setLoading(false); });
    return () => abort.abort();
  }, [attempt]);

  useEffect(() => {
    const abort = new AbortController();
    setAvailability(null); setAvailabilityError(false);
    if (connectionId) void loadConnectionOperations(connectionId, abort.signal).then(value => {
      if (!abort.signal.aborted) setAvailability(value);
    }).catch(() => { if (!abort.signal.aborted) setAvailabilityError(true); });
    return () => abort.abort();
  }, [connectionId, attempt]);

  const adapter = profiles.find(profile => profile.adapterKey === adapterKey);
  const operations = (adapter?.operations ?? []).filter(operation =>
    [operation.displayName, operation.description, operation.quantity, operation.capabilityId].some(text => text?.toLowerCase().includes(query.trim().toLowerCase())));
  const selected = operations.find(operation => operation.key === selection);
  function search(value: string) {
    setQuery(value); setSelection(null);
  }
  return <div {...splitFocus} className="split-layout adapter-operations" data-has-selection={!!selected}>
    <div className="operation-controls">
      <SelectField label="Adapter" value={adapterKey} disabled={loading || !!error} onChange={event => {
        setAdapterKey(event.target.value); setSelection(null); setQuery(''); setConnectionId(''); setAvailability(null);
      }}>{profiles.map(profile => <option key={profile.adapterKey} value={profile.adapterKey ?? ''}>{profile.displayName}</option>)}</SelectField>
      <SelectField label="Connection" value={connectionId} disabled={!adapter || loading} onChange={event => {
        setConnectionId(event.target.value); setAvailability(null); setAvailabilityError(false);
      }}><option value="">Adapter capabilities only</option>
        {connections.filter(connection => connection.adapterKey === adapterKey).map(connection =>
          <option key={connection.id} value={connection.id}>{connection.name}</option>)}</SelectField>
      <ActionButton label="Refresh operations" onClick={() => { setConnectionId(''); setAttempt(value => value + 1); }} disabled={loading} />
    </div>
    {adapter && <p className="split-intro">{adapter.description}</p>}
    {connectionId && <div className="operation-availability"><Notice>
      {availabilityError ? 'Connection readiness could not be checked. Refresh to try again.' : !availability
        ? 'Checking connection readiness…' : <><strong>{availability.state === 'ConnectionReady' ? 'Connection ready' : availability.state}</strong>
          <p>{availability.reason}</p><small>Connection revision {availability.connectionRevision}. This does not grant asset access.</small></>}
    </Notice></div>}
    {error && <Notice tone="error">{error}</Notice>}
    {loading ? <PanelLoading label="Loading adapter operations" /> : !error && <div className="split-panes">
      <section className="panel split-browse" aria-label="Operations">
        <PanelHeader title="Operations" count={operations.length} />
        <div className="operation-search"><SearchInput aria-label="Search operations" placeholder="Search operations" value={query} onChange={event => search(event.target.value)} /></div>
        {operations.length === 0 && <p className="empty-state">{query.trim() ? 'No operations match your search.' : 'This adapter has no implemented read operations to display.'}</p>}
        <ul className="split-results">{operations.map(operation => <li key={operation.key} data-selected={operation.key === selection}>
          <button type="button" className="connection-row" aria-current={operation.key === selection}
            data-split-focus-key={operation.key} onClick={() => setSelection(operation.key ?? null)}>
            <strong>{operation.displayName}</strong><small>{operation.capability === 'HistoricalData' ? 'Historical data' : 'Current data'} · v{operation.contractVersion}</small>
          </button></li>)}</ul>
      </section>
      <section className="panel split-selected" tabIndex={-1} aria-label="Selected operation">
        {selected ? <>
          <ActionButton className="split-back" label="Back to operations" onClick={() => setSelection(null)} />
          <PanelHeader title={selected.displayName ?? 'Operation'} />
          <AdapterOperationDetails key={selected.key} operation={selected} />
        </> : <p className="empty-state">Select an operation to inspect its contract and limits.</p>}
      </section>
    </div>}
  </div>;
}
