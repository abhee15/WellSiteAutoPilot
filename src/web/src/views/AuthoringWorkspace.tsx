import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  addAuthoringDraftToCatalog, buildAuthoringDraft, cancelAuthoringDraftBuild, createAuthoringDraft,
  downloadAuthoringDraftPackage, getAuthoringCapabilities, getAuthoringDraft, listAuthoringDrafts,
  updateAuthoringDraft, validateAuthoringDraft, testAuthoringDraft,
} from '../api/generated/v1/sdk.gen';
import type {
  AuthoringBuildState, AuthoringCapabilitiesResponse, AuthoringDraftResponse,
  AuthoringDraftSummaryResponse, AuthoringIssueResponse, AuthoringTestResponse, AuthoringTestState, AuthoringValidationState,
} from '../api/generated/v1/types.gen';
import { ActionButton } from '../components/ActionButton';
import { PanelHeader } from '../components/common';
import { Status } from '../components/Status';
import { t } from '../localization';
import type { MessageKey } from '../localization';
import {
  addInput, addOutput, addParameter, addStep, buildOutline, buildTone, documentsEqual, readDocument,
  removeElement, selectionForIssuePath, testTone, usedBy, usesOf, validationTone,
} from './authoring';
import type { DraftDocument, OutlineNode, Selection, SelectionKind } from './authoring';
import { ElementEditor, KIND_LABELS } from './AuthoringElementEditor';
import { LivePreview } from './AuthoringLivePreview';
import './authoring.css';

const POLL_MILLISECONDS = 1500;
const GROUPS: SelectionKind[] = ['input', 'parameter', 'step', 'condition', 'output', 'action'];

const VALIDATION_LABELS: Record<AuthoringValidationState, MessageKey> = {
  NotRun: 'authoring.state.notRun', Valid: 'authoring.state.valid', Invalid: 'authoring.state.invalid', Stale: 'authoring.state.stale',
};
const TEST_LABELS: Record<AuthoringTestState, MessageKey> = {
  NotRun: 'authoring.state.notRun', Passed: 'authoring.state.passed', Failed: 'authoring.state.failed',
  Stale: 'authoring.state.stale', Cancelled: 'authoring.state.cancelled',
};
const BUILD_LABELS: Record<AuthoringBuildState, MessageKey> = {
  None: 'authoring.state.none', Building: 'authoring.state.building', Succeeded: 'authoring.state.succeeded',
  Failed: 'authoring.state.failed', Cancelled: 'authoring.state.cancelled', Stale: 'authoring.state.stale',
};

type Notice = { tone: 'status' | 'alert'; text: string };

function errorCode(error: unknown): string | null {
  return error && typeof error === 'object' && 'code' in error && typeof error.code === 'string' ? error.code : null;
}

function failureText(status: number | undefined, code: string | null): string {
  if (code === 'AUTHORING_REVISION_CONFLICT') return t('authoring.conflict');
  if (code === 'AUTHORING_DRAFT_INVALID') return t('authoring.invalidDraft');
  if (code === 'AUTHORING_BUILD_NOT_CURRENT') return t('authoring.buildNotCurrent');
  if (code === 'AUTHORING_TEST_HOST_UNAVAILABLE') return t('authoring.testHostUnavailable');
  if (code === 'AUTHORING_FIXTURE_REQUIRED') return t('authoring.fixtureRequired');
  if (code === 'AUTHORING_LOCAL_DISABLED') return t('authoring.disabledResponse');
  if (status === 403) return t('authoring.denied');
  return t('authoring.failed');
}

/**
 * The local authoring proof of concept: pick or start a draft, edit its graph, then validate, build,
 * download, or hand the package to the module catalog (where it stays untrusted and disabled).
 */
export function AuthoringWorkspace({ onCatalogChanged }: { onCatalogChanged: () => Promise<void> }) {
  const [capabilities, setCapabilities] = useState<AuthoringCapabilitiesResponse | null>(null);
  const [loadFailed, setLoadFailed] = useState(false);
  const [drafts, setDrafts] = useState<AuthoringDraftSummaryResponse[]>([]);
  const [starterId, setStarterId] = useState('');
  const [draft, setDraft] = useState<AuthoringDraftResponse | null>(null);
  const [working, setWorking] = useState<DraftDocument | null>(null);
  const [selection, setSelection] = useState<Selection>({ kind: 'module', id: 'module' });
  const [busy, setBusy] = useState<string | null>(null);
  const [notice, setNotice] = useState<Notice | null>(null);
  const mounted = useRef(true);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);

  const saved = useMemo(() => readDocument(draft?.document), [draft]);
  const dirty = working !== null && !documentsEqual(working, saved);
  const outline = useMemo(() => (working ? buildOutline(working) : []), [working]);
  const enabled = capabilities?.localBuildEnabled === true;

  const adopt = useCallback((next: AuthoringDraftResponse) => {
    setDraft(next);
    setWorking(readDocument(next.document));
  }, []);

  const refreshList = useCallback(async () => {
    const result = await listAuthoringDrafts({ credentials: 'same-origin' });
    if (mounted.current && result.data) setDrafts(result.data);
  }, []);

  useEffect(() => {
    let live = true;
    (async () => {
      try {
        const result = await getAuthoringCapabilities({ credentials: 'same-origin' });
        if (!live) return;
        if (!result.data) { setLoadFailed(true); return; }
        setCapabilities(result.data);
        setStarterId(result.data.starters?.[0]?.id ?? '');
        if (result.data.localBuildEnabled) await refreshList();
      } catch { if (live) setLoadFailed(true); }
    })();
    return () => { live = false; };
  }, [refreshList]);

  const draftId = draft?.id;
  const building = draft?.buildState === 'Building';
  useEffect(() => {
    if (!building || !draftId) return undefined;
    const timer = window.setInterval(() => {
      void getAuthoringDraft({ path: { draftId }, credentials: 'same-origin' }).then(result => {
        if (!mounted.current || !result.data) return;
        setDraft(result.data);
        if (result.data.buildState !== 'Building') void refreshList();
      }).catch(() => undefined);
    }, POLL_MILLISECONDS);
    return () => window.clearInterval(timer);
  }, [building, draftId, refreshList]);

  async function run(name: string, action: () => Promise<Notice | null>) {
    if (busy) return;
    setBusy(name); setNotice(null);
    try { setNotice(await action()); }
    catch { setNotice({ tone: 'alert', text: t('authoring.failed') }); }
    finally { if (mounted.current) setBusy(null); }
  }

  const fail = (response: Response | undefined, error: unknown): Notice =>
    ({ tone: 'alert', text: failureText(response?.status, errorCode(error)) });

  async function open(id: string) {
    await run('open', async () => {
      const result = await getAuthoringDraft({ path: { draftId: id }, credentials: 'same-origin' });
      if (!result.data) return fail(result.response, result.error);
      adopt(result.data); setSelection({ kind: 'module', id: 'module' });
      return null;
    });
  }

  async function create() {
    await run('create', async () => {
      const result = await createAuthoringDraft({ body: { starterId }, credentials: 'same-origin' });
      if (!result.data) return fail(result.response, result.error);
      adopt(result.data); setSelection({ kind: 'module', id: 'module' });
      await refreshList();
      return null;
    });
  }

  async function save() {
    if (!draft?.id || !working) return;
    const id = draft.id;
    await run('save', async () => {
      const result = await updateAuthoringDraft({
        path: { draftId: id }, body: { expectedRevision: draft.revision, document: working }, credentials: 'same-origin',
      });
      if (!result.data) return fail(result.response, result.error);
      adopt(result.data); await refreshList();
      return { tone: 'status', text: t('authoring.saved', { revision: result.data.revision ?? 0 }) };
    });
  }

  async function validate() {
    if (!draft?.id) return;
    const id = draft.id;
    await run('validate', async () => {
      const result = await validateAuthoringDraft({ path: { draftId: id }, credentials: 'same-origin' });
      if (!result.data) return fail(result.response, result.error);
      adopt(result.data); await refreshList();
      return { tone: 'status', text: t('authoring.validated') };
    });
  }

  async function build() {
    if (!draft?.id) return;
    const id = draft.id;
    await run('build', async () => {
      const result = await buildAuthoringDraft({ path: { draftId: id }, credentials: 'same-origin' });
      if (!result.data) return fail(result.response, result.error);
      adopt(result.data);
      return { tone: 'status', text: t('authoring.buildStarted') };
    });
  }

  async function runTests() {
    if (!draft?.id) return;
    const id = draft.id;
    await run('test', async () => {
      const result = await testAuthoringDraft({ path: { draftId: id }, credentials: 'same-origin' });
      if (!result.data) return fail(result.response, result.error);
      adopt(result.data);
      return { tone: 'status', text: t('authoring.testRan') };
    });
  }

  async function cancel() {
    if (!draft?.id) return;
    const id = draft.id;
    await run('cancel', async () => {
      const result = await cancelAuthoringDraftBuild({ path: { draftId: id }, credentials: 'same-origin' });
      if (!result.data) return fail(result.response, result.error);
      adopt(result.data);
      return null;
    });
  }

  async function download() {
    if (!draft?.id) return;
    const id = draft.id;
    await run('download', async () => {
      const result = await downloadAuthoringDraftPackage({ path: { draftId: id }, parseAs: 'blob', credentials: 'same-origin' });
      if (!(result.data instanceof Blob)) return fail(result.response, result.error);
      const url = URL.createObjectURL(result.data);
      const link = document.createElement('a');
      link.href = url; link.download = `${working?.module.moduleId ?? 'module'}-${working?.module.version ?? ''}.wsamodule`;
      link.click();
      URL.revokeObjectURL(url);
      return { tone: 'status', text: t('authoring.downloaded') };
    });
  }

  async function addToCatalog() {
    if (!draft?.id) return;
    const id = draft.id;
    await run('catalog', async () => {
      const result = await addAuthoringDraftToCatalog({ path: { draftId: id }, credentials: 'same-origin' });
      if (!result.data) return fail(result.response, result.error);
      const refreshed = await getAuthoringDraft({ path: { draftId: id }, credentials: 'same-origin' });
      if (refreshed.data && mounted.current) setDraft(refreshed.data);
      try { await onCatalogChanged(); } catch { /* the catalog list refreshes on its next poll */ }
      return { tone: 'status', text: t('authoring.catalogAdded', { module: result.data.moduleId ?? '', version: result.data.version ?? '' }) };
    });
  }

  function change(next: DraftDocument) { setWorking(next); setNotice(null); }
  function add(maker: (doc: DraftDocument) => { doc: DraftDocument; id: string }, kind: SelectionKind) {
    if (!working) return;
    const made = maker(working);
    change(made.doc); setSelection({ kind, id: made.id });
  }

  if (loadFailed) return <section className="panel authoring-notice" role="alert"><p>{t('authoring.loadFailed')}</p></section>;
  if (!capabilities) return null;
  if (!enabled) {
    return <section className="panel authoring-notice">
      <h2>{t('authoring.disabledHeading')}</h2>
      <p>{t('authoring.disabledBody')}</p>
    </section>;
  }

  const selectedNode = outline.find(node => node.kind === selection.kind && node.id === selection.id);
  const reads = selectedNode ? usesOf(outline, selectedNode) : [];
  const readBy = usedBy(outline, selection);
  const removable = readBy.length === 0;
  const issues: AuthoringIssueResponse[] = [...(draft?.validationIssues ?? []), ...(draft?.build?.diagnostics ?? [])];
  const buildSucceeded = draft?.buildState === 'Succeeded';
  const catalogued = Boolean(draft?.build?.catalogModuleRecordId);
  const idle = busy === null;

  return <div className="split-panes authoring-layout">
    <section className="panel split-browse" aria-label={t('authoring.draftsList')}>
      <PanelHeader title={t('authoring.drafts')} count={drafts.length} />
      <p className="authoring-note">{t('authoring.trustedNotice')}</p>
      <div className="authoring-inline">
        <label className="authoring-field"><span>{t('authoring.starter')}</span>
          <select value={starterId} onChange={event => setStarterId(event.target.value)}>
            {(capabilities.starters ?? []).map(starter => <option key={starter.id} value={starter.id ?? ''}>{starter.label}</option>)}
          </select>
        </label>
        <ActionButton label={t('authoring.newDraft')} variant="primary" disabled={!idle || !starterId} onClick={() => void create()} />
      </div>
      {drafts.length === 0 ? <p className="authoring-note">{t('authoring.noDrafts')}</p> : <ul className="authoring-list" aria-label={t('authoring.draftsList')}>
        {drafts.map(item => <li key={item.id}>
          <button type="button" className="authoring-draft" aria-current={item.id === draft?.id}
            aria-label={t('authoring.open', { name: item.displayName ?? item.moduleId ?? '' })}
            onClick={() => void open(item.id ?? '')}>
            <strong>{item.displayName ?? item.moduleId}</strong>
            <small>{item.moduleId} {item.version}</small>
          </button>
        </li>)}
      </ul>}
    </section>

    <section className="panel split-selected authoring-workspace" aria-label={t('authoring.workspace')}>
      {!draft || !working ? <p className="authoring-note">{t('authoring.pick')}</p> : <>
        <div className="authoring-header">
          <div>
            <h2>{working.module.displayName}</h2>
            <small>{working.module.moduleId} {working.module.version} · {t('authoring.revision', { revision: draft.revision ?? 0 })}</small>
          </div>
          <div className="authoring-states">
            <span>{t('authoring.state.validation')}</span>
            <Status tone={validationTone(draft.validationState)} label={t(VALIDATION_LABELS[draft.validationState ?? 'NotRun'])} />
            <span>{t('authoring.state.test')}</span>
            <Status tone={testTone(draft.testState)} label={t(TEST_LABELS[draft.testState ?? 'NotRun'])} />
            <span>{t('authoring.state.build')}</span>
            <Status tone={buildTone(draft.buildState)} label={t(BUILD_LABELS[draft.buildState ?? 'None'])} />
          </div>
        </div>
        <p className="authoring-note">{t('authoring.testNote')}</p>
        {dirty && <p role="status" className="authoring-note">{t('authoring.unsaved')}</p>}
        <div className="authoring-actions">
          <ActionButton label={t(busy === 'save' ? 'authoring.saving' : 'authoring.save')} variant="primary"
            disabled={!idle || !dirty} onClick={() => void save()} />
          <ActionButton label={t('authoring.validate')} disabled={!idle || dirty} onClick={() => void validate()} />
          <ActionButton label={t('authoring.build')} disabled={!idle || dirty || building} onClick={() => void build()} />
          {building && <ActionButton label={t('authoring.cancelBuild')} disabled={!idle} onClick={() => void cancel()} />}
          <ActionButton label={t('authoring.test')} disabled={!idle || dirty || !buildSucceeded} onClick={() => void runTests()} />
          <ActionButton label={t('authoring.download')} disabled={!idle || dirty || !buildSucceeded} onClick={() => void download()} />
          <ActionButton label={t('authoring.addToCatalog')} disabled={!idle || dirty || !buildSucceeded || catalogued}
            onClick={() => void addToCatalog()} />
        </div>
        {notice && <p role={notice.tone}>{notice.text}</p>}
        {buildSucceeded && draft.build?.packageSha256 && <p className="authoring-note">
          {t('authoring.artifact', { sha: draft.build.packageSha256.slice(0, 12), bytes: draft.build.packageBytes ?? 0 })}
          {' '}{t('authoring.artifactRevision', { revision: draft.build.revision ?? 0 })}
          {catalogued && <> {t('authoring.catalogRecorded')}</>}
        </p>}

        <div className="authoring-columns">
          <nav className="authoring-outline" aria-label={t('authoring.outline')}>
            <h3>{t('authoring.outline')}</h3>
            <p className="authoring-note">{t('authoring.outlineHint')}</p>
            <ul>
              <li>
                <button type="button" className="authoring-node" aria-current={selection.kind === 'module'}
                  onClick={() => setSelection({ kind: 'module', id: 'module' })}>{t('authoring.group.module')}</button>
              </li>
            </ul>
            {GROUPS.map(kind => <OutlineGroup key={kind} kind={kind} nodes={outline.filter(node => node.kind === kind)}
              selection={selection} onSelect={setSelection}
              onAdd={kind === 'input' ? () => add(addInput, 'input') : kind === 'parameter' ? () => add(addParameter, 'parameter')
                : kind === 'step' ? () => add(addStep, 'step') : kind === 'output' ? () => add(addOutput, 'output') : undefined} />)}
          </nav>

          <div className="authoring-editor">
            <h3>{selection.kind === 'step' ? t('authoring.workspace') : t('authoring.properties')}</h3>
            <p className="authoring-kind">{t(KIND_LABELS[selection.kind])}</p>
            {selection.kind !== 'module' && <DependencyLine reads={reads} readBy={readBy} />}
            <ElementEditor doc={working} selection={selection} onChange={change} removable={removable}
              onRemove={() => { change(removeElement(working, selection)); setSelection({ kind: 'module', id: 'module' }); }} />
          </div>
        </div>

        <TestResults test={draft.test ?? null} state={draft.testState ?? 'NotRun'} />
        <LivePreview doc={working} />

        <div className="authoring-issues">
          <h3>{issues.length > 0 && draft.buildState === 'Failed' ? t('authoring.diagnostics') : t('authoring.issues')}</h3>
          {issues.length === 0 ? <p className="authoring-note">{t('authoring.noIssues')}</p> : <ul>
            {issues.map((issue, index) => {
              const target = selectionForIssuePath(issue.path);
              return <li key={`${issue.code}-${index}`} data-severity={issue.severity}>
                <Status tone={issue.severity === 'Error' ? 'error' : issue.severity === 'Warning' ? 'warning' : 'info'} label={issue.severity ?? ''} />
                {' '}{target
                  ? <button type="button" className="authoring-link" onClick={() => setSelection(target)}>{issue.message}</button>
                  : <span>{issue.message}</span>}
              </li>;
            })}
          </ul>}
        </div>
      </>}
    </section>
  </div>;
}

function valueText(value: unknown): string {
  return typeof value === 'string' ? value : JSON.stringify(value);
}

function TestResults({ test, state }: { test: AuthoringTestResponse | null; state: AuthoringTestState }) {
  const values = (items: { id?: string | null; value?: unknown; unit?: string | null }[]) => items.length === 0
    ? <p className="authoring-note">{t('authoring.testNoValues')}</p>
    : <dl className="authoring-values">{items.map(item => <div key={item.id}>
      <dt>{item.id}</dt><dd>{valueText(item.value)}{item.unit ? ` ${item.unit}` : ''}</dd>
    </div>)}</dl>;
  return <div className="authoring-tests" aria-label={t('authoring.tests')}>
    <h3>{t('authoring.tests')}</h3>
    {!test ? <p className="authoring-note">{t('authoring.testNone')}</p> : <>
      {state === 'Stale' && <p className="authoring-note" role="status">{t('authoring.testStale')}</p>}
      {(test.diagnostics ?? []).map(issue => <p key={issue.code} role="alert">{issue.message}</p>)}
      {(test.cases ?? []).map(item => <section key={item.fixtureId} className="authoring-case">
        <h4>{item.label} <Status tone={item.passed ? 'success' : 'error'}
          label={t(item.passed ? 'authoring.testCasePassed' : 'authoring.testCaseFailed')} /></h4>
        {(item.mismatches ?? []).map(text => <p key={text} role="alert">{text}</p>)}
        <h5>{t('authoring.testOutputs')}</h5>{values(item.outputs ?? [])}
        <h5>{t('authoring.testIntermediates')}</h5>{values(item.intermediates ?? [])}
        <h5>{t('authoring.testActions')}</h5>
        {(item.actions ?? []).length === 0 ? <p className="authoring-note">{t('authoring.testNoValues')}</p>
          : <p>{(item.actions ?? []).join(', ')}</p>}
      </section>)}
    </>}
  </div>;
}

function DependencyLine({ reads, readBy }: { reads: OutlineNode[]; readBy: OutlineNode[] }) {
  const names = (nodes: OutlineNode[]) => nodes.map(node => node.label).join(', ');
  return <p className="authoring-note">
    {reads.length > 0 ? t('authoring.reads', { names: names(reads) }) : t('authoring.readsNothing')}
    {' · '}
    {readBy.length > 0 ? t('authoring.usedBy', { names: names(readBy) }) : t('authoring.notUsed')}
  </p>;
}

function OutlineGroup({ kind, nodes, selection, onSelect, onAdd }: {
  kind: SelectionKind; nodes: OutlineNode[]; selection: Selection; onSelect: (selection: Selection) => void; onAdd?: () => void;
}) {
  const titles: Record<SelectionKind, MessageKey> = {
    module: 'authoring.group.module', input: 'authoring.group.input', parameter: 'authoring.group.parameter',
    step: 'authoring.group.step', condition: 'authoring.group.condition', output: 'authoring.group.output',
    action: 'authoring.group.action',
  };
  const addLabels: Partial<Record<SelectionKind, MessageKey>> = {
    input: 'authoring.add.input', parameter: 'authoring.add.parameter', step: 'authoring.add.step', output: 'authoring.add.output',
  };
  return <div className="authoring-group">
    <h4>{t(titles[kind])}</h4>
    <ul>
      {nodes.map(node => <li key={node.id}>
        <button type="button" className="authoring-node" aria-current={selection.kind === node.kind && selection.id === node.id}
          onClick={() => onSelect({ kind: node.kind, id: node.id })}>{node.label}</button>
      </li>)}
    </ul>
    {onAdd && addLabels[kind] && <ActionButton label={t(addLabels[kind])} onClick={onAdd} />}
  </div>;
}
