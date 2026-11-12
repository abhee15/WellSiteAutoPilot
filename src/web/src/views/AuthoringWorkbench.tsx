import { useEffect, useMemo, useRef, useState } from 'react';
import { ActionButton } from '../components/ActionButton';
import { PanelHeader } from '../components/common';
import { SelectField, TextAreaField, TextField } from '../components/FormFields';
import { Notice } from '../components/Notice';
import { PanelLoading } from '../components/LoadingIndicator';
import { AdapterGuide } from './AdapterGuide';
import { AuthoringNodeEditor } from './AuthoringNodeEditor';
import { AuthoringResults } from './AuthoringResults';
import { evaluateDraft, loadStarters, parseDraft, showVal, validateDraft } from './authoring';
import type { Analysis, ModuleDraft, ModuleRun, Starter } from './authoring';
import { addNode, emptyDraft, kindLabel, nodeKindOf, removeNode, updateNode } from './authoringDraft';
import type { NodeKind, Selection } from './authoringDraft';
import { discoverConnection, listPoints, loadConnections } from './connections';
import type { CatalogPoint, Connection, DiscoveredObject } from './connections';
import './connections.css';
import './authoring.css';

const kinds: NodeKind[] = ['input', 'parameter', 'step', 'output'];
const groupTitle: Record<NodeKind, string> = { input: 'Inputs', parameter: 'Parameters', step: 'Calculations', output: 'Outputs' };

/**
 * The authoring workspace: one revisitable view of a module's inputs, parameters, calculations and outputs, with
 * live checks, a dependency trace and tests against a real asset or typed sample values.
 */
export function AuthoringWorkbench() {
  const [starters, setStarters] = useState<Starter[]>([]);
  const [draft, setDraft] = useState<ModuleDraft>(emptyDraft);
  const [selection, setSelection] = useState<Selection>({ kind: 'module' });
  const [analysis, setAnalysis] = useState<Analysis | null>(null);
  const [run, setRun] = useState<ModuleRun | null>(null);
  const [running, setRunning] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [guide, setGuide] = useState(false);
  const [transfer, setTransfer] = useState<string | null>(null);
  const [connections, setConnections] = useState<Connection[] | null>(null);
  const [connectionId, setConnectionId] = useState('');
  const [assets, setAssets] = useState<DiscoveredObject[]>([]);
  const [assetId, setAssetId] = useState('');
  const [assetFilter, setAssetFilter] = useState('');
  const [points, setPoints] = useState<CatalogPoint[] | null>(null);
  const [samples, setSamples] = useState<Record<string, string>>({});
  const [overrides, setOverrides] = useState<Record<string, string>>({});
  const checking = useRef(0);

  const connection = connections?.find(item => item.id === connectionId) ?? null;

  useEffect(() => {
    const controller = new AbortController();
    loadStarters(controller.signal).then(items => { setStarters(items); if (items[0]) setDraft(items[0].draft); })
      .catch(() => { if (!controller.signal.aborted) setMessage('The starter modules could not be loaded.'); });
    loadConnections(controller.signal).then(items => {
      const usable = items.filter(item => item.isEnabled);
      setConnections(usable);
      if (usable[0]) setConnectionId(usable[0].id);
    }).catch(() => { if (!controller.signal.aborted) setConnections([]); });
    return () => controller.abort();
  }, []);

  useEffect(() => {
    setAssets([]); setAssetId(''); setPoints(null);
    if (!connection) return;
    let live = true;
    discoverConnection(connection).then(found => { if (live) setAssets(found.items); })
      .catch(() => { if (live) setMessage('Assets could not be discovered on this connection.'); });
    return () => { live = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [connection?.id]);

  useEffect(() => { setPoints(null); }, [assetId]);

  // The server owns the rules, so the outline always shows its latest verdict, a moment after typing stops.
  useEffect(() => {
    const ticket = ++checking.current;
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      validateDraft(draft, controller.signal).then(result => { if (ticket === checking.current) setAnalysis(result); })
        .catch(() => { if (!controller.signal.aborted && ticket === checking.current) setAnalysis(null); });
    }, 350);
    return () => { window.clearTimeout(timer); controller.abort(); };
  }, [draft]);

  const stale = run !== null && analysis !== null && run.fingerprint !== analysis.fingerprint;
  const problems = analysis?.problems ?? [];
  const errors = problems.filter(problem => problem.severity === 'Error');
  const valueOf = useMemo(() => {
    const map = new Map<string, string>();
    if (run) {
      run.inputs.forEach(item => map.set(item.name, showVal(item.value, item.unit)));
      run.parameters.concat(run.steps).forEach(item => map.set(item.name, showVal(item.value)));
      run.outputs.forEach(item => map.set(item.name, showVal(item.value, item.unit)));
    }
    return map;
  }, [run]);
  const shownAssets = useMemo(() => {
    const needle = assetFilter.trim().toLowerCase();
    return (needle ? assets.filter(item => `${item.displayName} ${item.externalId}`.toLowerCase().includes(needle)) : assets).slice(0, 200);
  }, [assets, assetFilter]);

  function load(next: ModuleDraft) {
    setDraft(next); setSelection({ kind: 'module' }); setRun(null); setMessage(null); setSamples({}); setOverrides({});
  }

  function edit(kind: NodeKind, name: string, patch: Record<string, unknown>) {
    setDraft(current => updateNode(current, kind, name, patch));
    if (typeof patch.name === 'string') setSelection({ kind, name: patch.name });
  }

  function add(kind: NodeKind) {
    const added = addNode(draft, kind);
    setDraft(added.draft); setSelection({ kind, name: added.name });
  }

  function select(name: string) {
    const kind = nodeKindOf(draft, name);
    if (kind) setSelection({ kind, name });
  }

  async function browse() {
    if (!connection || !assetId) return;
    setMessage(null);
    try { setPoints(await listPoints(connection.id, assetId)); }
    catch { setMessage('Points could not be listed. Only ForeSite connections list points today.'); }
  }

  async function test() {
    setRunning(true); setMessage(null);
    try {
      setRun(await evaluateDraft(draft, connection && assetId
        ? { connectionId: connection.id, externalId: assetId, parameters: overrides }
        : { samples, parameters: overrides }));
    } catch { setMessage('The test could not be completed. ForeSite can take a little while; try again.'); }
    finally { setRunning(false); }
  }

  function importDraft() {
    try { load(parseDraft(transfer ?? '')); setTransfer(null); }
    catch { setMessage('That is not a module description. Paste the JSON that Export produced.'); }
  }

  if (!connections) return <PanelLoading label="Loading the authoring workspace" />;

  const live = connection !== null && assetId !== '';
  const selected = selection.kind === 'module' ? null : selection;

  return <section className="authoring" aria-label="Authoring workspace">
    <PanelHeader title="Authoring workspace" />
    <Notice announce={false}>Proof of concept. Build a logic module from inputs, parameters, calculations and outputs, then test it on a real asset.
      Nothing is saved or written to a field controller; set point changes are only planned.</Notice>
    {message && <Notice tone="warning">{message}</Notice>}
    <div className="connection-actions">
      <SelectField label="Start from" value="" onChange={event => { const found = starters.find(item => item.id === event.target.value); if (found) load(found.draft); }}>
        <option value="">Choose a starter…</option>
        {starters.map(item => <option key={item.id} value={item.id}>{item.title}</option>)}
      </SelectField>
      <ActionButton label="New empty module" variant="secondary" onClick={() => load(emptyDraft)} />
      <ActionButton label="Export or import" variant="secondary" onClick={() => setTransfer(transfer === null ? JSON.stringify(draft, null, 2) : null)} />
      <ActionButton label={guide ? 'Hide the adapter guide' : 'How adapters work'} variant="quiet" onClick={() => setGuide(!guide)} />
    </div>
    {guide && <AdapterGuide connections={connections} />}
    {transfer !== null && <div className="connection-form">
      <TextAreaField label="Module description (JSON)" rows={12} value={transfer} onChange={event => setTransfer(event.target.value)}
        helper="Copy this to move the module between systems, or paste a description and choose Import. Connections are not part of it: they are bound where it is tested and run." />
      <div className="connection-actions">
        <ActionButton label="Import this description" variant="primary" onClick={importDraft} />
        <ActionButton label="Close" variant="quiet" onClick={() => setTransfer(null)} />
      </div>
    </div>}

    <div className="authoring-workspace">
      <nav className="connection-form authoring-outline" aria-label="Module outline">
        <button type="button" className="authoring-node" aria-current={selection.kind === 'module'} onClick={() => setSelection({ kind: 'module' })}>
          <strong>{draft.name || 'Untitled'}</strong>
          <small>{errors.length === 0 ? (analysis ? 'No errors' : 'Checking…') : `${errors.length} to fix`}</small>
        </button>
        {kinds.map(kind => {
          const items = { input: draft.inputs, parameter: draft.parameters, step: draft.steps, output: draft.outputs }[kind];
          return <div key={kind}>
            <h4>{groupTitle[kind]}</h4>
            {items.map(item => {
              const own = problems.filter(problem => problem.node === item.name);
              return <button key={item.name} type="button" className="authoring-node" aria-current={selection.kind === kind && selection.name === item.name}
                data-problem={own.some(problem => problem.severity === 'Error') ? 'error' : own.length ? 'warning' : undefined}
                onClick={() => setSelection({ kind, name: item.name })}>
                <span>{item.name}</span>
                <small>{own[0]?.message ?? valueOf.get(item.name) ?? ''}</small>
              </button>;
            })}
            <ActionButton label={`Add ${kindLabel[kind].toLowerCase()}`} variant="quiet" onClick={() => add(kind)} />
          </div>;
        })}
      </nav>

      <div className="authoring-center">
        {selected
          ? <AuthoringNodeEditor draft={draft} kind={selected.kind} name={selected.name} analysis={analysis} run={run} points={points}
            onChange={patch => edit(selected.kind, selected.name, patch)}
            onRemove={() => { setDraft(removeNode(draft, selected.kind, selected.name)); setSelection({ kind: 'module' }); }}
            onSelect={select} />
          : <div className="connection-form">
            <h3>Module</h3>
            <TextField label="Module name" value={draft.name} onChange={event => setDraft({ ...draft, name: event.target.value })} />
            <TextAreaField label="What it does" rows={3} value={draft.description ?? ''} onChange={event => setDraft({ ...draft, description: event.target.value })} />
            {problems.length > 0 && <ul className="authoring-reasons">{problems.map((problem, index) => <li key={index} data-severity={problem.severity}>
              {problem.node ? <button type="button" className="authoring-link" onClick={() => select(problem.node)}>{problem.node}</button> : null} {problem.message}</li>)}</ul>}
            {analysis?.valid && <Notice tone="success">The module is valid. {analysis.order.length > 0 ? `Calculation order: ${analysis.order.join(', ')}.` : ''}</Notice>}
          </div>}
      </div>

      <div className="authoring-test">
        <div className="connection-form">
          <h3>Test</h3>
          <SelectField label="Connection" value={connectionId} onChange={event => setConnectionId(event.target.value)}>
            <option value="">No connection: use typed values</option>
            {connections.map(item => <option key={item.id} value={item.id}>{item.name} ({item.adapterKey})</option>)}
          </SelectField>
          {connection && <>
            <TextField label="Find an asset" value={assetFilter} onChange={event => setAssetFilter(event.target.value)} />
            <SelectField label={`Asset (${assets.length})`} value={assetId} onChange={event => setAssetId(event.target.value)}>
              <option value="">Choose an asset</option>
              {shownAssets.map(item => <option key={item.externalId} value={item.externalId}>{item.displayName} [{item.externalId}]</option>)}
            </SelectField>
          </>}
          {!live && draft.inputs.map(input => <TextField key={input.name} label={`Value for ${input.name}`} value={samples[input.name] ?? ''}
            onChange={event => setSamples({ ...samples, [input.name]: event.target.value })} />)}
          {draft.parameters.map(parameter => <TextField key={parameter.name} label={`Try ${parameter.name} (saved: ${parameter.value})`}
            value={overrides[parameter.name] ?? ''} onChange={event => setOverrides({ ...overrides, [parameter.name]: event.target.value })} />)}
          <div className="connection-actions">
            <ActionButton label="Run test" variant="primary" busy={running} disabled={errors.length > 0 || draft.outputs.length === 0} onClick={() => void test()} />
            <ActionButton label="Browse this asset's points" variant="secondary" disabled={!live} onClick={() => void browse()} />
          </div>
          {points && <p className="connection-help">{points.length} points loaded. Select an input and use "Add a source from this asset's points".</p>}
        </div>
      </div>
    </div>
    {run && <AuthoringResults run={run} stale={stale} onSelect={select} />}
  </section>;
}
