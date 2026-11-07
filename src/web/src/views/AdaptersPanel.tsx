import { useEffect, useState } from 'react';
import { RefreshCw } from 'lucide-react';
import type { AdapterOperationsResponse } from '../api/generated/v1/types.gen';
import { ActionButton } from '../components/ActionButton';
import { PanelHeader } from '../components/common';
import { PanelLoading } from '../components/LoadingIndicator';
import { SelectField } from '../components/FormFields';
import { Notice } from '../components/Notice';
import { SearchInput } from '../components/SearchInput';
import { useSplitPaneFocus } from '../components/useSplitPaneFocus';
import { t } from '../localization';
import { AdapterCapabilityDetails } from './AdapterCapabilityDetails';
import { loadAdapterOperations } from './adapterOperations';
import { ConnectionsError } from './connections';
import './connections.css';
import './splitLayout.css';
import './adapterOperations.css';

/** Static adapter reference. No saved Connection, Asset or provider data is loaded by this screen. */
export function AdaptersPanel() {
  const [profiles, setProfiles] = useState<AdapterOperationsResponse[]>([]);
  const [adapterKey, setAdapterKey] = useState('');
  const [query, setQuery] = useState('');
  const [selection, setSelection] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  const splitFocus = useSplitPaneFocus(selection);

  useEffect(() => {
    const abort = new AbortController();
    setLoading(true); setError(null);
    void loadAdapterOperations(abort.signal).then(adapters => {
      if (abort.signal.aborted) return;
      setProfiles(adapters);
      setAdapterKey(current => adapters.some(adapter => adapter.adapterKey === current) ? current : adapters[0]?.adapterKey ?? '');
    }).catch((caught: unknown) => {
      if (!abort.signal.aborted) {
        setProfiles([]); setSelection(null);
        setError(caught instanceof ConnectionsError && caught.kind === 'accessDenied' ? t('adapterReference.denied') : t('adapterReference.failed'));
      }
    }).finally(() => { if (!abort.signal.aborted) setLoading(false); });
    return () => abort.abort();
  }, [attempt]);

  const adapter = profiles.find(profile => profile.adapterKey === adapterKey);
  const capabilities = (adapter?.capabilities ?? []).filter(capability => {
    const variants = adapter?.operations?.filter(operation => capability.operationKeys?.includes(operation.key ?? '')) ?? [];
    return [capability.displayName, capability.description, ...variants.flatMap(operation => [operation.displayName, operation.quantity])]
      .some(text => text?.toLowerCase().includes(query.trim().toLowerCase()));
  });
  const selected = capabilities.find(capability => capability.key === selection);
  const operations = adapter?.operations?.filter(operation => selected?.operationKeys?.includes(operation.key ?? '')) ?? [];

  return <div {...splitFocus} className="split-layout adapter-operations" data-has-selection={!!selected && !loading && !error}>
    <div className="operation-controls">
      <SelectField label={t('adapterReference.adapter')} value={adapterKey} disabled={loading || !!error || profiles.length === 0} onChange={event => {
        setAdapterKey(event.target.value); setSelection(null); setQuery('');
      }}>{profiles.map(profile => <option key={profile.adapterKey} value={profile.adapterKey ?? ''}>{profile.displayName}</option>)}</SelectField>
      <ActionButton label={t('adapterReference.refresh')} icon={RefreshCw} iconOnly onClick={() => setAttempt(value => value + 1)} busy={loading} />
    </div>
    <p className="split-intro">{t('adapterReference.intro')}</p>
    {adapter && !loading && !error && <p className="split-intro">{adapter.description}</p>}
    {error && <Notice tone="error">{error}</Notice>}
    {loading ? <PanelLoading label={t('adapterReference.loading')} /> : !error && <div className="split-panes">
      <section className="panel split-browse" aria-label={t('adapterReference.capabilities')}>
        <PanelHeader title={t('adapterReference.capabilities')} count={capabilities.length} />
        <div className="operation-search"><SearchInput aria-label={t('adapterReference.search')} placeholder={t('adapterReference.search')} value={query}
          onChange={event => { setQuery(event.target.value); setSelection(null); }} /></div>
        {capabilities.length === 0 && <p className="empty-state">{query.trim() ? t('adapterReference.noMatches') : t('adapterReference.empty')}</p>}
        <ul className="split-results">{capabilities.map(capability => <li key={capability.key} data-selected={capability.key === selection}>
          <button type="button" className="connection-row" aria-current={capability.key === selection}
            data-split-focus-key={capability.key} onClick={() => setSelection(capability.key ?? null)}>
            <strong>{capability.displayName}</strong><small>{t('adapterReference.version', { version: capability.contractVersion ?? '' })}</small>
          </button></li>)}</ul>
      </section>
      <section className="panel split-selected" tabIndex={-1} aria-label={t('adapterReference.selected')}>
        {selected ? <>
          <ActionButton className="split-back" label={t('adapterReference.back')} onClick={() => setSelection(null)} />
          <PanelHeader title={selected.displayName ?? t('adapterReference.capability')} />
          <AdapterCapabilityDetails key={adapterKey + '/' + selected.key} capability={selected} operations={operations} />
        </> : <p className="empty-state">{t('adapterReference.select')}</p>}
      </section>
    </div>}
  </div>;
}
