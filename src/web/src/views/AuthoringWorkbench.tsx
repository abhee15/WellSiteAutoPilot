import { useEffect, useMemo, useRef, useState } from 'react';
import { ActionButton } from '../components/ActionButton';
import { PanelHeader } from '../components/common';
import { SelectField, TextAreaField, TextField } from '../components/FormFields';
import { Notice } from '../components/Notice';
import { CollapsibleSection, Section } from '../components/Section';
import { StatusIndicator } from '../components/Status';
import { SectionTabs } from '../components/SectionTabs';
import { t } from '../localization';
import { PanelLoading } from '../components/LoadingIndicator';
import { AdapterGuide } from './AdapterGuide';
import { AuthoringNodeEditor } from './AuthoringNodeEditor';
import { AuthoringResults } from './AuthoringResults';
import {
  evaluateDraft,
  exampleFromRun,
  loadStarters,
  parseDraft,
  requestKey,
  showVal,
  validateDraft,
  verifyDraft,
} from './authoring';
import type {
  Analysis,
  BindingSource,
  EvaluateTarget,
  ModuleBindings,
  ModuleDraft,
  ModuleRun,
  Starter,
  VerifyResult,
} from './authoring';
import { addNode, emptyDraft, kindLabel, moveBindings, nodeKindOf, removeNode, updateNode } from './authoringDraft';
import type { DraftInput, DraftOutput, DraftParameter, DraftStep } from './authoring';
import type { NodeKind, Selection } from './authoringDraft';
import { discoverConnection, listPoints, loadConnections } from './connections';
import type { CatalogPoint, Connection, DiscoveredObject } from './connections';
import './connections.css';
import './authoring.css';

function describeNode(kind: NodeKind, item: DraftInput | DraftParameter | DraftStep | DraftOutput): string {
  if (kind === 'input') {
    const input = item as DraftInput;
    return [input.quantity, input.unit, input.basis].filter(Boolean).join(' · ');
  }
  if (kind === 'parameter') {
    const parameter = item as DraftParameter;
    return `${parameter.value}${parameter.unit ? ` ${parameter.unit}` : ''}`;
  }
  if (kind === 'step') {
    const step = item as DraftStep;
    return step.rules?.length ? `${step.rules.length} rules, first match decides` : step.expression;
  }
  const output = item as DraftOutput;
  return output.target ? `${output.kind} to ${output.target}` : output.kind;
}
const docGroups: {
  title: 'authoring.docReads' | 'authoring.docDecides' | 'authoring.docDoes';
  hint: 'authoring.docReadsHint' | 'authoring.docDecidesHint' | 'authoring.docDoesHint';
  kinds: NodeKind[];
}[] = [
  { title: 'authoring.docReads', hint: 'authoring.docReadsHint', kinds: ['input', 'parameter'] },
  { title: 'authoring.docDecides', hint: 'authoring.docDecidesHint', kinds: ['step'] },
  { title: 'authoring.docDoes', hint: 'authoring.docDoesHint', kinds: ['output'] },
];
const recoveryKey = 'wsa.authoring.draft';

type Recovered = { draft: ModuleDraft; bindings: ModuleBindings };

function readRecovery(): Recovered | null {
  try {
    const stored = window.localStorage.getItem(recoveryKey);
    const body = stored ? (JSON.parse(stored) as Recovered) : null;
    return body && Array.isArray(body.draft?.inputs) ? body : null;
  } catch {
    return null;
  }
}

/**
 * The authoring workspace: one revisitable view of a module's inputs, parameters, calculations and outputs, with
 * live checks, a dependency trace, stored examples, and tests against a real asset or typed sample values. The module
 * is portable; bindings (which provider points answer, which set point is changed) belong to this system.
 */
export function AuthoringWorkbench() {
  const [starters, setStarters] = useState<Starter[]>([]);
  const [draft, setDraft] = useState<ModuleDraft>(emptyDraft);
  const [bindings, setBindings] = useState<ModuleBindings>({});
  const [selection, setSelection] = useState<Selection>({ kind: 'module' });
  const [analysis, setAnalysis] = useState<Analysis | null>(null);
  const [run, setRun] = useState<{ result: ModuleRun; key: string } | null>(null);
  const [verified, setVerified] = useState<{ result: VerifyResult; key: string } | null>(null);
  const [running, setRunning] = useState(false);
  const [verifying, setVerifying] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [guide, setGuide] = useState(false);
  const [started, setStarted] = useState(false);
  const [panel, setPanel] = useState<'test' | 'results' | 'problems' | 'examples'>('test');
  const [transfer, setTransfer] = useState<string | null>(null);
  const [recovered, setRecovered] = useState<Recovered | null>(() => readRecovery());
  const [connections, setConnections] = useState<Connection[] | null>(null);
  const [connectionId, setConnectionId] = useState('');
  const [assets, setAssets] = useState<DiscoveredObject[]>([]);
  const [assetId, setAssetId] = useState('');
  const [assetFilter, setAssetFilter] = useState('');
  const [points, setPoints] = useState<CatalogPoint[] | null>(null);
  const [samples, setSamples] = useState<Record<string, string>>({});
  const [overrides, setOverrides] = useState<Record<string, string>>({});
  const checking = useRef(0);
  const testing = useRef(0);
  const edited = useRef(false);

  const connection = connections?.find((item) => item.id === connectionId) ?? null;
  const live = connection !== null && assetId !== '';
  const pins = useMemo(
    () => Object.fromEntries(Object.entries(samples).filter(([, value]) => value.trim() !== '')),
    [samples],
  );
  const target = useMemo<EvaluateTarget>(
    () =>
      live
        ? { connectionId: connection.id, externalId: assetId, samples: pins, parameters: overrides, bindings }
        : { samples, parameters: overrides, bindings },
    [live, connection, assetId, overrides, samples, pins, bindings],
  );
  const currentKey = useMemo(() => requestKey(draft, target), [draft, target]);

  useEffect(() => {
    const controller = new AbortController();
    loadStarters(controller.signal)
      .then((items) => {
        setStarters(items);
      })
      .catch(() => {
        if (!controller.signal.aborted) setMessage('The starter modules could not be loaded.');
      });
    loadConnections(controller.signal)
      .then((items) => {
        const usable = items.filter((item) => item.isEnabled);
        setConnections(usable);
        if (usable[0]) setConnectionId(usable[0].id);
      })
      .catch(() => {
        if (!controller.signal.aborted) setConnections([]);
      });
    return () => controller.abort();
  }, []);

  useEffect(() => {
    setAssets([]);
    setAssetId('');
    setPoints(null);
    if (!connection) return;
    let current = true;
    discoverConnection(connection)
      .then((found) => {
        if (current) setAssets(found.items);
      })
      .catch(() => {
        if (current) setMessage('Assets could not be discovered on this connection.');
      });
    return () => {
      current = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [connection?.id]);

  useEffect(() => {
    setPoints(null);
  }, [assetId]);

  // The server owns the rules, so the outline always shows its latest verdict, a moment after typing stops.
  useEffect(() => {
    const ticket = ++checking.current;
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      validateDraft(draft, controller.signal)
        .then((result) => {
          if (ticket === checking.current) setAnalysis(result);
        })
        .catch(() => {
          if (!controller.signal.aborted && ticket === checking.current) setAnalysis(null);
        });
    }, 350);
    return () => {
      window.clearTimeout(timer);
      controller.abort();
    };
  }, [draft]);

  // Work in progress survives a reload: only edited drafts are kept, never the untouched starter.
  useEffect(() => {
    if (!edited.current) return;
    const timer = window.setTimeout(() => {
      try {
        window.localStorage.setItem(recoveryKey, JSON.stringify({ draft, bindings }));
      } catch {
        /* storage may be unavailable */
      }
    }, 600);
    return () => window.clearTimeout(timer);
  }, [draft, bindings]);

  const stale = run !== null && run.key !== currentKey;
  const problems = analysis?.problems ?? [];
  const errors = problems.filter((problem) => problem.severity === 'Error');
  const valueOf = useMemo(() => {
    const map = new Map<string, string>();
    if (run) {
      run.result.inputs.forEach((item) => map.set(item.name, showVal(item.value, item.unit)));
      run.result.parameters.concat(run.result.steps).forEach((item) => map.set(item.name, showVal(item.value)));
      run.result.outputs.forEach((item) => map.set(item.name, showVal(item.value, item.unit)));
    }
    return map;
  }, [run]);
  const shownAssets = useMemo(() => {
    const needle = assetFilter.trim().toLowerCase();
    return (
      needle ? assets.filter((item) => `${item.displayName} ${item.externalId}`.toLowerCase().includes(needle)) : assets
    ).slice(0, 200);
  }, [assets, assetFilter]);
  const draftKey = useMemo(() => JSON.stringify(draft), [draft]);

  function load(next: ModuleDraft, nextBindings: ModuleBindings = {}) {
    edited.current = false;
    setStarted(true);
    setDraft(next);
    setBindings(nextBindings);
    setSelection({ kind: 'module' });
    setRun(null);
    setVerified(null);
    setMessage(null);
    setSamples({});
    setOverrides({});
  }

  function change(next: ModuleDraft) {
    edited.current = true;
    setDraft(next);
  }

  function edit(kind: NodeKind, name: string, patch: Record<string, unknown>) {
    change(updateNode(draft, kind, name, patch));
    if (typeof patch.name === 'string') {
      setSelection({ kind, name: patch.name });
      if (kind === 'input') setBindings((current) => moveBindings(current, name, patch.name as string));
    }
  }

  function setSources(name: string, sources: BindingSource[]) {
    edited.current = true;
    setBindings((current) => ({ ...current, inputs: { ...(current.inputs ?? {}), [name]: sources } }));
  }

  function add(kind: NodeKind) {
    const added = addNode(draft, kind);
    change(added.draft);
    setSelection({ kind, name: added.name });
  }

  function select(name: string) {
    const kind = nodeKindOf(draft, name);
    if (kind) setSelection({ kind, name });
  }

  async function browse() {
    if (!connection || !assetId) return;
    setMessage(null);
    try {
      setPoints(await listPoints(connection.id, assetId));
    } catch {
      setMessage('Points could not be listed. Only ForeSite connections list points today.');
    }
  }

  async function test() {
    const ticket = ++testing.current;
    const key = currentKey;
    setRunning(true);
    setMessage(null);
    try {
      const result = await evaluateDraft(draft, target);
      if (ticket === testing.current) {
        setRun({ result, key });
        setPanel('results');
      }
    } catch {
      if (ticket === testing.current)
        setMessage('The test could not be completed. ForeSite can take a little while; try again.');
    } finally {
      if (ticket === testing.current) setRunning(false);
    }
  }

  async function verify() {
    const key = draftKey;
    setVerifying(true);
    setMessage(null);
    try {
      setVerified({ result: await verifyDraft(draft), key });
    } catch {
      setMessage('The stored examples could not be run.');
    } finally {
      setVerifying(false);
    }
  }

  function importDraft() {
    try {
      load(parseDraft(transfer ?? ''));
      edited.current = true;
      setTransfer(null);
    } catch {
      setMessage('That is not a module description. Paste the JSON that Export produced.');
    }
  }

  if (!connections) return <PanelLoading label="Loading the authoring workspace" />;

  const selected = selection.kind === 'module' ? null : selection;
  const examples = draft.tests ?? [];
  const exampleStale = verified !== null && verified.key !== draftKey;

  return (
    <section className="authoring" aria-label="Authoring workspace">
      <PanelHeader title="Authoring workspace" />
      {message && <Notice tone="warning">{message}</Notice>}
      {recovered && (
        <Notice tone="info">
          You have an unsaved draft from an earlier visit.
          <span className="connection-actions">
            <ActionButton
              label="Restore it"
              variant="secondary"
              onClick={() => {
                load(recovered.draft, recovered.bindings ?? {});
                edited.current = true;
                setRecovered(null);
              }}
            />
            <ActionButton
              label="Discard it"
              variant="quiet"
              onClick={() => {
                window.localStorage.removeItem(recoveryKey);
                setRecovered(null);
              }}
            />
          </span>
        </Notice>
      )}
      {!started && (
        <div className="authoring-gallery">
          <Notice announce={false}>{t('authoring.poc')}</Notice>
          <Section title={t('authoring.galleryTitle')} hint={t('authoring.galleryHint')}>
            <div className="authoring-cards">
              <button type="button" className="authoring-card" onClick={() => load(emptyDraft)}>
                <strong>{t('authoring.blankTitle')}</strong>
                <span>{t('authoring.blankHint')}</span>
              </button>
              {starters.map((item) => (
                <button
                  key={item.id}
                  type="button"
                  className="authoring-card"
                  onClick={() => load(item.draft, item.bindings ?? {})}
                >
                  <strong>{item.title}</strong>
                  <span>{item.summary}</span>
                </button>
              ))}
            </div>
            <div className="connection-actions">
              <ActionButton
                label="Import a module description"
                variant="secondary"
                onClick={() => setTransfer(transfer === null ? '' : null)}
              />
              <ActionButton
                label={guide ? 'Hide the adapter guide' : 'How adapters work'}
                variant="quiet"
                onClick={() => setGuide(!guide)}
              />
            </div>
          </Section>
        </div>
      )}
      {guide && <AdapterGuide connections={connections} />}
      {transfer !== null && (
        <div className="connection-form">
          <TextAreaField
            label="Module description (JSON)"
            rows={12}
            value={transfer}
            onChange={(event) => setTransfer(event.target.value)}
            helper="Copy this to move the module between systems, or paste a description and choose Import. Bindings are not part of it: they are set where it is tested and run."
          />
          <div className="connection-actions">
            <ActionButton label="Import this description" variant="primary" onClick={importDraft} />
            <ActionButton label="Close" variant="quiet" onClick={() => setTransfer(null)} />
          </div>
        </div>
      )}

      {started && (
        <div className="authoring-workspace">
          <div className="authoring-doc">
            <div className="authoring-header">
              <TextField
                label="Module name"
                value={draft.name}
                onChange={(event) => change({ ...draft, name: event.target.value })}
              />
              <StatusIndicator
                tone={errors.length > 0 ? 'error' : analysis ? 'success' : 'neutral'}
                label={errors.length > 0 ? `${errors.length} to fix` : analysis ? t('authoring.valid') : 'Checking…'}
              />
              <div className="connection-actions">
                <ActionButton
                  label={t('authoring.changeStarter')}
                  variant="secondary"
                  onClick={() => setStarted(false)}
                />
                <ActionButton
                  label="Export or import"
                  variant="quiet"
                  onClick={() => setTransfer(transfer === null ? JSON.stringify(draft, null, 2) : null)}
                />
              </div>
            </div>
            <CollapsibleSection
              title={t('authoring.aboutTitle')}
              meta={draft.description ? undefined : t('authoring.aboutEmpty')}
            >
              <TextAreaField
                label="What it does"
                rows={2}
                value={draft.description ?? ''}
                onChange={(event) => change({ ...draft, description: event.target.value })}
              />
            </CollapsibleSection>
            {docGroups.map((group) => (
              <Section key={group.title} title={t(group.title)} hint={t(group.hint)}>
                {group.kinds.map((kind) => {
                  const items = {
                    input: draft.inputs,
                    parameter: draft.parameters,
                    step: draft.steps,
                    output: draft.outputs,
                  }[kind];
                  return (
                    <div key={kind} className="authoring-items">
                      {items.map((item) => {
                        const own = problems.filter((problem) => problem.node === item.name);
                        const open = selection.kind === kind && 'name' in selection && selection.name === item.name;
                        const reads = analysis?.graph.find((node) => node.kind === kind && node.name === item.name);
                        return (
                          <div key={item.name} className="authoring-item" data-open={open}>
                            <button
                              type="button"
                              className="authoring-node"
                              aria-expanded={open}
                              data-problem={
                                own.some((problem) => problem.severity === 'Error')
                                  ? 'error'
                                  : own.length
                                    ? 'warning'
                                    : undefined
                              }
                              onClick={() => setSelection(open ? { kind: 'module' } : { kind, name: item.name })}
                            >
                              <span>
                                <strong>{item.name}</strong>
                                <small className="authoring-why">
                                  {kindLabel[kind]} · {describeNode(kind, item)}
                                  {reads && reads.dependsOn.length > 0
                                    ? ` · ${t('authoring.flowFrom', { names: reads.dependsOn.join(', ') })}`
                                    : ''}
                                  {own[0] ? ` · ${own[0].message}` : ''}
                                </small>
                              </span>
                              <strong>{valueOf.get(item.name) ?? ''}</strong>
                            </button>
                            {open && selected && (
                              <AuthoringNodeEditor
                                draft={draft}
                                kind={selected.kind}
                                name={selected.name}
                                analysis={analysis}
                                run={run?.result ?? null}
                                points={points}
                                sources={selected.kind === 'input' ? (bindings.inputs?.[selected.name] ?? []) : []}
                                onSources={(sources) => setSources(selected.name, sources)}
                                onChange={(patch) => edit(selected.kind, selected.name, patch)}
                                onRemove={() => {
                                  if (selected.kind === 'input')
                                    setBindings((current) => moveBindings(current, selected.name, null));
                                  change(removeNode(draft, selected.kind, selected.name));
                                  setSelection({ kind: 'module' });
                                }}
                                onSelect={select}
                              />
                            )}
                          </div>
                        );
                      })}
                      <ActionButton
                        label={t('authoring.flowAdd', { kind: kindLabel[kind].toLowerCase() })}
                        variant="quiet"
                        onClick={() => add(kind)}
                      />
                    </div>
                  );
                })}
              </Section>
            ))}
          </div>
          <div className="authoring-panel">
            <SectionTabs
              prefix="authoring-panel"
              label={t('authoring.panelLabel')}
              selected={panel}
              onSelect={setPanel}
              tabs={[
                { id: 'test', label: t('authoring.panelTest') },
                { id: 'results', label: t('authoring.panelResults') },
                { id: 'problems', label: t('authoring.panelProblems', { count: problems.length }) },
                { id: 'examples', label: t('authoring.panelExamples', { count: examples.length }) },
              ]}
            />
            <div
              role="tabpanel"
              id={`authoring-panel-${panel}`}
              aria-labelledby={`authoring-panel-tab-${panel}`}
              className="authoring-panel-body"
            >
              {panel === 'test' && (
                <div className="connection-form">
                  <SelectField
                    label="Connection"
                    value={connectionId}
                    onChange={(event) => setConnectionId(event.target.value)}
                  >
                    <option value="">No connection: use typed values</option>
                    {connections.map((item) => (
                      <option key={item.id} value={item.id}>
                        {item.name} ({item.adapterKey})
                      </option>
                    ))}
                  </SelectField>
                  {connection && (
                    <>
                      {assets.length > 12 && (
                        <TextField
                          label="Find an asset"
                          value={assetFilter}
                          onChange={(event) => setAssetFilter(event.target.value)}
                        />
                      )}
                      <SelectField
                        label={`Asset (${assets.length})`}
                        value={assetId}
                        onChange={(event) => setAssetId(event.target.value)}
                      >
                        <option value="">Choose an asset</option>
                        {shownAssets.map((item) => (
                          <option key={item.externalId} value={item.externalId}>
                            {item.displayName} [{item.externalId}]
                          </option>
                        ))}
                      </SelectField>
                    </>
                  )}
                  {Object.keys(pins).length > 0 && live && (
                    <Notice tone="info">
                      {t('authoring.pinnedTitle')}: {Object.keys(pins).join(', ')} use typed values, not live reads.
                      Clear a value to read it live again.
                    </Notice>
                  )}
                  <CollapsibleSection
                    open={!live}
                    title={t('authoring.valuesTitle')}
                    hint={t(live ? 'authoring.valuesHintLive' : 'authoring.valuesHintTyped')}
                  >
                    {draft.inputs.map((input) => (
                      <TextField
                        key={input.name}
                        label={live ? `Pin ${input.name} (optional)` : `Value for ${input.name}`}
                        value={samples[input.name] ?? ''}
                        helper={
                          input.unit ? `A number in ${input.unit}, or with its own unit, such as 20 kPa.` : undefined
                        }
                        onChange={(event) => setSamples({ ...samples, [input.name]: event.target.value })}
                      />
                    ))}
                    {draft.parameters.map((parameter) => (
                      <TextField
                        key={parameter.name}
                        label={`Try ${parameter.name} (saved: ${parameter.value}${parameter.unit ? ` ${parameter.unit}` : ''})`}
                        value={overrides[parameter.name] ?? ''}
                        onChange={(event) => setOverrides({ ...overrides, [parameter.name]: event.target.value })}
                      />
                    ))}
                  </CollapsibleSection>
                  <div className="connection-actions">
                    <ActionButton
                      label="Run test"
                      variant="primary"
                      busy={running}
                      disabled={errors.length > 0 || draft.outputs.length === 0}
                      onClick={() => void test()}
                    />
                    <ActionButton
                      label="Browse this asset's points"
                      variant="secondary"
                      disabled={!live}
                      onClick={() => void browse()}
                    />
                  </div>
                  {points && (
                    <p className="connection-help">
                      {points.length} points loaded. Select an input and use "Add a source from this asset's points".
                    </p>
                  )}
                </div>
              )}
              {panel === 'results' &&
                (run ? (
                  <AuthoringResults
                    run={run.result}
                    stale={stale}
                    onSelect={select}
                    onSaveExample={
                      stale || !exampleFromRun(run.result, 'x')
                        ? undefined
                        : () => {
                            const saved = exampleFromRun(
                              run.result,
                              `Example ${examples.length + 1}${run.result.externalId ? ` from ${run.result.externalId}` : ''}`,
                            );
                            if (saved) change({ ...draft, tests: [...examples, saved] });
                          }
                    }
                  />
                ) : (
                  <p className="connection-help">{t('authoring.noResults')}</p>
                ))}
              {panel === 'problems' &&
                (problems.length > 0 ? (
                  <ul className="authoring-reasons">
                    {problems.map((problem, index) => (
                      <li key={`${problem.node}-${index}`} data-severity={problem.severity}>
                        {problem.node ? (
                          <button type="button" className="authoring-link" onClick={() => select(problem.node)}>
                            {problem.node}
                          </button>
                        ) : null}{' '}
                        {problem.message}
                      </li>
                    ))}
                  </ul>
                ) : (
                  <p className="connection-help">{t('authoring.noProblems')}</p>
                ))}
              {panel === 'examples' && (
                <div className="connection-form">
                  <h4>Stored examples</h4>
                  <p className="connection-help">
                    {examples.length === 0
                      ? 'This module has no stored examples. Examples are typed inputs with the outputs they must produce, so the module can be checked on any system without live data.'
                      : `${examples.length} example${examples.length === 1 ? '' : 's'} stored with the module.`}
                  </p>
                  <div className="connection-actions">
                    <ActionButton
                      label="Run stored examples"
                      variant="secondary"
                      busy={verifying}
                      disabled={errors.length > 0 || examples.length === 0}
                      onClick={() => void verify()}
                    />
                  </div>
                  {verified && (
                    <div data-stale={exampleStale} aria-label="Stored example results">
                      {exampleStale && (
                        <Notice tone="warning">The module changed after these examples ran. Run them again.</Notice>
                      )}
                      <Notice tone={verified.result.tests.failed === 0 ? 'success' : 'warning'}>
                        {verified.result.tests.passed} passed, {verified.result.tests.failed} failed.
                        {verified.result.manifestValid
                          ? ' Maps onto a valid logic module manifest.'
                          : ` Does not map onto a valid manifest: ${verified.result.manifestError ?? 'unknown'}.`}
                      </Notice>
                      <ul className="authoring-checks">
                        {verified.result.tests.cases.map((item) => (
                          <li key={item.name} data-status={item.passed ? 'Pass' : 'Fail'}>
                            <strong>{item.passed ? 'Pass' : 'Fail'}</strong> {item.name}
                            {item.error ? `: ${item.error}` : ''}
                            {item.checks
                              .filter((check) => !check.passed)
                              .map((check) => (
                                <small key={check.output} className="authoring-why">
                                  {check.output}: expected {check.expected}, got {check.actual}
                                </small>
                              ))}
                          </li>
                        ))}
                      </ul>
                    </div>
                  )}
                </div>
              )}
            </div>
          </div>
        </div>
      )}
    </section>
  );
}
