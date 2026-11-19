import { useEffect, useId, useRef, useState } from 'react';
import {
  approveGuidedModuleSubmission,
  createGuidedModuleDraft,
  deleteGuidedModuleDraft,
  editGuidedModuleDraft,
  getGuidedModuleSubmission,
  listGuidedModuleDrafts,
  listGuidedModuleProviders,
  previewGuidedModuleDraft,
  submitGuidedModuleDraft,
} from '../api/generated/v1/sdk.gen';
import type {
  GuidedModuleDraftResponse,
  GuidedModulePreviewResponse,
  GuidedModuleReviewResponse,
  GuidedPressureReadingRequest,
  GuidedProviderResponse,
  GuidedSampleCaseRequest,
  GuidedSourceHintRequest,
} from '../api/generated/v1/types.gen';
import { ActionButton } from '../components/ActionButton';
import { ContextLink } from '../components/ContextLink';
import { PageTabNav } from '../components/PageTabNav';
import { SectionTabs } from '../components/SectionTabs';
import { CodeBlock } from '../components/CodeBlock';
import {
  addDailyInput,
  blankContractManifest,
  contractIsComplete,
  copyContractManifest,
  dailyCapability,
  GuidedContractEditor,
  firstIncompletePort,
  isContractOnlyManifest,
  parseManifest,
} from './GuidedContractEditor';
import { GuidedLivePreview } from './GuidedLivePreview';
import { GuidedModuleContract } from './GuidedModuleContract';
import { GuidedReviewQueue } from './GuidedReviewQueue';
import { GuidedSampleCases, defaultSampleCases, sampleCasesInRange } from './GuidedSampleCases';
import { normalizeHint } from './GuidedSourceHintFields';
import { ConfirmPanel, FormActions, NumberField, SelectField, TextField } from '../components/FormFields';
import { Notice } from '../components/Notice';
import { PanelHeader } from '../components/common';
import { t } from '../localization';
import { requestNavigation, useUnsavedChangesGuard } from '../navigationGuard';
import { hashFor, useHashDetail } from '../routing';
import { outcomeLabel } from '../words';
import './guidedAuthoring.css';

const pageSize = 25;
type EditorView = 'workflow' | 'ide';

function readEditorView(userId: string | undefined): EditorView {
  if (!userId) return 'workflow';
  try {
    return window.localStorage.getItem('wsa.authoring.view.' + userId) === 'ide' ? 'ide' : 'workflow';
  } catch {
    return 'workflow';
  }
}

const pressureEntryPoint = 'wsa.declarative.difference.v1';
const thresholdEntryPoint = 'wsa.declarative.threshold.v1';

function supportsGuidedRule(manifestJson: string | null | undefined): boolean {
  if (!manifestJson) return true;
  try {
    const entry = (JSON.parse(manifestJson) as { entryPoint?: string }).entryPoint;
    return entry === thresholdEntryPoint || entry === pressureEntryPoint;
  } catch {
    return false;
  }
}

function isPressure(manifestJson: string | null | undefined) {
  return manifestJson && (JSON.parse(manifestJson) as { entryPoint?: string }).entryPoint === pressureEntryPoint;
}

function draftManifest(name: string, pressure: boolean) {
  const quantities = pressure
    ? [
        { id: 'casing-pressure', quantity: 'CasingPressure' },
        { id: 'tubing-pressure', quantity: 'TubingPressure' },
      ]
    : [{ id: 'fillage', quantity: 'PumpFillage' }];
  return JSON.stringify({
    manifestVersion: 1,
    moduleId: 'guided.' + crypto.randomUUID(),
    version: '1.0.0',
    displayName: name,
    // The pressure starter reads CygNet quantities, the fillage starter ForeSite's.
    requiredAdapterKey: pressure ? 'cygnet' : 'foresite',
    publisher: 'WellSite AutoPilot',
    runtime: 'DotNet',
    executionProfile: 'ScheduledOneShot',
    entryPoint: pressure ? pressureEntryPoint : 'wsa.declarative.threshold.v1',
    minimumSdkVersion: '1.1.0',
    assetRequirements: [{ role: 'well', minimumCount: 1, maximumCount: 1, requiredTraits: [] }],
    dataRequirements: quantities.map((input) => ({
      ...input,
      assetRole: 'well',
      access: 'Current',
      canonicalUnit: pressure ? 'psi' : '%',
      maximumAgeSeconds: pressure ? 120 : 60,
      allowUncertainQuality: false,
    })),
    commandRequirements: [],
    parameterSchema: {
      type: 'object',
      properties: { threshold: { type: 'number', minimum: 0, maximum: pressure ? 10000 : 100 } },
      additionalProperties: false,
    },
  });
}

function PressureEvidence({
  reading,
  differential,
}: {
  reading: GuidedPressureReadingRequest;
  differential?: number | null;
}) {
  return (
    <>
      {t('guided.casing')} {reading.casingPsi == null ? t('guided.unavailable') : reading.casingPsi + ' psi'}
      {' · '}
      {t('guided.tubing')} {reading.tubingPsi == null ? t('guided.unavailable') : reading.tubingPsi + ' psi'}
      {differential != null && (
        <>
          {' '}
          {' · '}
          {t('guided.difference')} {differential} psi
        </>
      )}
    </>
  );
}

/** A failed request that keeps the server's stable problem code so the page can say what to fix. */
class GuidedRequestError extends Error {
  constructor(readonly code: string | null) {
    super(code ?? 'guided request failed');
  }
}

function requestFailure(error: unknown): GuidedRequestError {
  const code = (error as { code?: unknown } | null | undefined)?.code;
  return new GuidedRequestError(typeof code === 'string' ? code : null);
}

/** Codes an author can act on; anything else gets the generic message. */
const knownErrorCodes: Record<string, Parameters<typeof t>[0]> = {
  GUIDED_DRAFT_REVISION_CONFLICT: 'guided.errorRevisionConflict',
  GUIDED_DRAFT_ALREADY_SUBMITTED: 'guided.errorAlreadySubmitted',
  GUIDED_DRAFT_SUBMITTED: 'guided.errorSubmittedKept',
  GUIDED_SAMPLE_EXPECTATION_FAILED: 'guided.errorExpectation',
  GUIDED_SAMPLE_CASES_INVALID: 'guided.errorCases',
  GUIDED_DRAFT_LOGIC_UNSUPPORTED: 'guided.errorNoLogic',
  GUIDED_SOURCE_HINT_INVALID: 'guided.errorSourceHint',
  MODULE_DATA_MAX_AGE_INVALID: 'guided.errorMaxAge',
  MODULE_DATA_REQUIREMENT_DUPLICATE: 'guided.errorDuplicateInput',
  MODULE_DATA_ROLE_UNKNOWN: 'guided.errorUnknownRole',
  MODULE_VALUE_SCHEMA_INVALID: 'guided.errorValueSchema',
};

function providerKey(manifestJson: string | null | undefined): string {
  const key = parseManifest(manifestJson)?.requiredAdapterKey;
  return typeof key === 'string' ? key : '';
}

function displayName(manifestJson: string | null | undefined): string {
  return manifestJson ? (JSON.parse(manifestJson) as { displayName: string }).displayName : '';
}

async function loadReview(reference: string, signal?: AbortSignal) {
  const [id, revision, extra] = reference.trim().split('/');
  if (!id || !revision || extra !== undefined || !Number.isSafeInteger(Number(revision)) || Number(revision) < 1)
    throw new Error();
  const { data } = await getGuidedModuleSubmission({ path: { id, revision: Number(revision) }, signal });
  if (!data) throw new Error();
  return data;
}

export function ModuleAuthoringWorkspace({
  permissions,
  userId,
  onRefresh,
}: {
  permissions: readonly string[];
  userId?: string;
  onRefresh: () => Promise<void>;
}) {
  const [detail] = useHashDetail('authoring');
  const canAuthor = permissions.includes('logic.authoring.manage');
  const canReview = permissions.includes('logic.authoring.review');
  const showReviews = canReview && (Boolean(detail) || !canAuthor);
  const reviewReference = showReviews && detail !== 'reviews' ? (detail ?? '') : '';
  return (
    <>
      {canAuthor && canReview && (
        <PageTabNav
          label={t('guided.workspaceTabs')}
          activeId={showReviews ? 'reviews' : 'drafts'}
          tabs={[
            { id: 'drafts', label: t('guided.draftsTab'), href: hashFor('authoring') },
            { id: 'reviews', label: t('guided.reviewsTab'), href: hashFor('authoring', 'reviews') },
          ]}
        />
      )}
      <GuidedAuthoringPage
        key={reviewReference || (showReviews ? 'reviews' : 'drafts')}
        initialReviewReference={reviewReference}
        canAuthor={canAuthor && !showReviews}
        userId={userId}
        canReview={showReviews}
        canApprove={permissions.includes('logic.manage')}
        showReviewQueue={showReviews && !reviewReference}
        canPreviewLive={permissions.includes('assets.read') && permissions.includes('connections.read')}
        onRefresh={onRefresh}
      />
    </>
  );
}

export function GuidedAuthoringPage({
  canAuthor,
  userId,
  canReview,
  canApprove,
  initialReviewReference = '',
  showReviewQueue = false,
  canPreviewLive = false,
  onRefresh,
}: {
  canAuthor: boolean;
  userId?: string;
  canReview: boolean;
  canApprove: boolean;
  initialReviewReference?: string;
  showReviewQueue?: boolean;
  canPreviewLive?: boolean;
  onRefresh: () => Promise<void>;
}) {
  const [view, setView] = useState(() => readEditorView(userId));
  const [editorSection, setEditorSection] = useState<'module' | 'rule' | 'live'>('module');
  const [scenario, setScenario] = useState(0);
  const editorId = useId();
  const starterChooser = useRef<HTMLDetailsElement>(null);
  const moduleSectionId = useId();
  const ruleSectionId = useId();
  const liveSectionId = useId();
  const [drafts, setDrafts] = useState<GuidedModuleDraftResponse[]>([]);
  const [offset, setOffset] = useState(0);
  // Which provider's drafts the list shows; '' is all of them. The server filters before paging.
  const [providerFilter, setProviderFilter] = useState('');
  const [refresh, setRefresh] = useState(0);
  const [draft, setDraft] = useState<GuidedModuleDraftResponse | null>(null);
  const [pressure, setPressure] = useState(false);
  // Providers authoring can target, and the one chosen for the next new module (a draft keeps its provider for life).
  const [providers, setProviders] = useState<GuidedProviderResponse[]>([]);
  const [starterProvider, setStarterProvider] = useState('');
  const [copyTarget, setCopyTarget] = useState('');
  // The editable typed contract of a contract-only draft; null for threshold and inspect-only drafts.
  const [contractManifest, setContractManifest] = useState<string | null>(null);
  const [name, setName] = useState('');
  const [threshold, setThreshold] = useState('70');
  const [evidence, setEvidence] = useState<GuidedModulePreviewResponse[]>([]);
  // Authored sample cases; null means the draft's saved cases (or the starting cases for a new draft) are unchanged.
  const [cases, setCases] = useState<GuidedSampleCaseRequest[] | null>(null);
  // Authored source choices; null means the draft's saved choices are unchanged.
  const [hints, setHints] = useState<GuidedSourceHintRequest[] | null>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [reviewKey, setReviewKey] = useState(initialReviewReference);
  const [submittedReference, setSubmittedReference] = useState('');
  const [review, setReview] = useState<GuidedModuleReviewResponse | null>(null);
  const [reason, setReason] = useState('');
  const [approved, setApproved] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(false);
  const [errorCode, setErrorCode] = useState<string | null>(null);
  const [notice, setNotice] = useState<
    'guided.saved' | 'guided.submitted' | 'guided.approved' | 'guided.deleted' | null
  >(null);

  useEffect(() => {
    if (!canAuthor) return;
    const controller = new AbortController();
    void listGuidedModuleDrafts({
      query: { offset, limit: pageSize, adapterKey: providerFilter || undefined },
      signal: controller.signal,
    })
      .then(({ data }) => {
        if (controller.signal.aborted) return;
        if (data) setDrafts(data);
        else setError(true);
      })
      .catch(() => {
        if (!controller.signal.aborted) setError(true);
      });
    return () => controller.abort();
  }, [canAuthor, offset, refresh, providerFilter]);

  useEffect(() => {
    if (!canAuthor) return;
    const controller = new AbortController();
    void listGuidedModuleProviders({ signal: controller.signal })
      .then(({ data }) => {
        if (controller.signal.aborted) return;
        if (data) setProviders(data);
        else setError(true);
      })
      .catch(() => {
        if (!controller.signal.aborted) setError(true);
      });
    return () => controller.abort();
  }, [canAuthor]);

  useEffect(() => {
    if (!canReview || !initialReviewReference) return;
    const controller = new AbortController();
    setBusy(true);
    void loadReview(initialReviewReference, controller.signal)
      .then((data) => {
        if (!controller.signal.aborted) setReview(data);
      })
      .catch(() => {
        if (!controller.signal.aborted) setError(true);
      })
      .finally(() => {
        if (!controller.signal.aborted) setBusy(false);
      });
    return () => controller.abort();
  }, [canReview, initialReviewReference]);

  function select(
    next: GuidedModuleDraftResponse | null,
    nextPressure = Boolean(isPressure(next?.manifestJson)),
    section: typeof editorSection = 'module',
  ) {
    setPressure(nextPressure);
    setDraft(next);
    setContractManifest(next?.manifestJson && isContractOnlyManifest(next.manifestJson) ? next.manifestJson : null);
    setName(displayName(next?.manifestJson));
    setThreshold(String(next?.definition?.default ?? (nextPressure ? 150 : 70)));
    setEvidence([]);
    setCases(null);
    setHints(null);
    setScenario(0);
    setEditorSection(section);
    setSubmittedReference('');
    setNotice(null);
    setError(false);
  }

  async function run(action: () => Promise<void>) {
    setBusy(true);
    setError(false);
    setNotice(null);
    try {
      await action();
    } catch (failure) {
      setError(true);
      setErrorCode(failure instanceof GuidedRequestError ? failure.code : null);
    } finally {
      setBusy(false);
    }
  }

  const boundary = draft?.definition?.default ?? (pressure ? 150 : 70);
  const maximum = pressure ? 10000 : 100;
  const baselineCases = draft?.sampleCases ?? defaultSampleCases(pressure, boundary);
  const activeCases = cases ?? baselineCases;
  const sampleCount = activeCases.length;
  const baselineHints = (draft?.sourceHints ?? []).map(normalizeHint);
  // A hint for an input that was removed from the contract is dropped, never saved.
  const declared = parseManifest(contractManifest ?? draft?.manifestJson)?.dataRequirements;
  const declaredInputs = new Set(
    Array.isArray(declared) ? (declared as { id?: unknown }[]).map((input) => input.id) : [],
  );
  const activeHints = (hints ?? baselineHints).map(normalizeHint).filter((hint) => declaredInputs.has(hint.inputId));
  const contractOnly = contractManifest !== null;
  const providerName = (manifestJson: string | null | undefined) =>
    providers.find((item) => item.adapterKey === providerKey(manifestJson))?.displayName ?? providerKey(manifestJson);
  const activeProvider = (() => {
    const key = parseManifest(contractManifest ?? draft?.manifestJson)?.requiredAdapterKey;
    return typeof key === 'string' ? key.toLowerCase() : null;
  })();
  const otherProviders = providers.filter((item) => item.adapterKey !== activeProvider);
  const dailyDeclared = (
    Array.isArray(declared) ? (declared as { quantity?: unknown; requiredCapability?: unknown }[]) : []
  )
    .filter((input) => input.requiredCapability === dailyCapability && typeof input.quantity === 'string')
    .map((input) => input.quantity as string);
  const inspectOnly = Boolean(draft && !contractOnly && !supportsGuidedRule(draft.manifestJson));
  const valid = contractOnly
    ? name.trim().length > 0 && contractIsComplete(contractManifest)
    : !inspectOnly &&
      sampleCasesInRange(activeCases, pressure) &&
      name.trim().length > 0 &&
      threshold.trim() !== '' &&
      Number(threshold) >= 0 &&
      Number(threshold) <= maximum;
  const contractChanged =
    contractOnly &&
    Boolean(draft) &&
    JSON.stringify(parseManifest(contractManifest)) !== JSON.stringify(parseManifest(draft?.manifestJson));
  const casesChanged = cases !== null && JSON.stringify(cases) !== JSON.stringify(baselineCases);
  const hintsChanged = hints !== null && JSON.stringify(activeHints) !== JSON.stringify(baselineHints);
  const dirty =
    name !== displayName(draft?.manifestJson) ||
    threshold !== String(boundary) ||
    contractChanged ||
    casesChanged ||
    hintsChanged;
  const blockedPort = contractOnly ? firstIncompletePort(contractManifest) : null;
  const blockedReason = inspectOnly || valid ? null : blockedReasonFor();
  // Submitting is only offered once every asserted case, after a run, produced the outcome its author expected.
  const expectationsMet = activeCases.every(
    (item, index) => !item.expectedOutcome || evidence[index]?.observedOutcome === item.expectedOutcome,
  );
  const markSaved = useUnsavedChangesGuard(canAuthor && dirty);

  const showLivePreview = Boolean(canPreviewLive && draft?.id && draft.revision && !dirty && !inspectOnly);
  const activeSection =
    editorSection === 'live' && !showLivePreview ? (inspectOnly ? 'module' : 'rule') : editorSection;

  function blockedReasonFor(): {
    key: 'guided.saveNeedsName' | 'guided.saveNeedsPort' | 'guided.saveNeedsRule';
    port?: string;
  } {
    if (name.trim().length === 0) return { key: 'guided.saveNeedsName' };
    if (contractOnly) return { key: 'guided.saveNeedsPort', port: blockedPort ?? '' };
    return { key: 'guided.saveNeedsRule' };
  }

  function changeView(next: EditorView) {
    setView(next);
    if (!userId) return;
    try {
      window.localStorage.setItem('wsa.authoring.view.' + userId, next);
    } catch {
      // The editor still works when storage is unavailable.
    }
  }

  function sampleReading(index: number, differential?: number | null) {
    const sample = activeCases[index];
    if (!sample) return null;
    if (pressure) return <PressureEvidence reading={sample} differential={differential} />;
    return sample.value === null || sample.value === undefined ? t('guided.unavailable') : sample.value + '%';
  }

  function sampleResult(item: GuidedModulePreviewResponse) {
    return (
      <span title={item.outcomeCode ?? item.failureCode ?? undefined}>
        {item.recommendationTitle ?? outcomeLabel(item.outcomeCode ?? item.failureCode).text}
      </span>
    );
  }

  function changeDraft(next: GuidedModuleDraftResponse | null, nextPressure?: boolean) {
    setConfirmDelete(false);
    requestNavigation(() => select(next, nextPressure));
  }

  async function deleteDraft() {
    if (!draft?.id || !draft.revision) return;
    const { error, response } = await deleteGuidedModuleDraft({
      path: { id: draft.id },
      query: { expectedRevision: draft.revision },
    });
    if (error || !response?.ok) throw new Error();
    const removed = draft.id;
    setDrafts((items) => items.filter((item) => item.id !== removed));
    setConfirmDelete(false);
    select(null);
    setNotice('guided.deleted');
  }

  async function copyContract(targetProvider: string) {
    const copied = draft?.manifestJson
      ? copyContractManifest(draft.manifestJson, targetProvider, t('guided.copySuffix'))
      : null;
    if (!copied) return;
    const { data, error: failure } = await createGuidedModuleDraft({ body: { manifestJson: copied } });
    if (!data) throw requestFailure(failure);
    select(data, false, activeSection);
    setRefresh((previous) => previous + 1);
    setNotice('guided.saved');
  }

  async function save() {
    if (contractOnly) {
      // A contract-only draft sends no definition, so the server stores no logic for it.
      const manifest = {
        ...(parseManifest(contractManifest) ?? {}),
        displayName: name.trim(),
      };
      const contractBody = { manifestJson: JSON.stringify(manifest), sourceHints: activeHints };
      const { data, error: failure } =
        draft?.id && draft.revision
          ? await editGuidedModuleDraft({
              path: { id: draft.id },
              body: { ...contractBody, expectedRevision: draft.revision },
            })
          : await createGuidedModuleDraft({ body: contractBody });
      if (!data) throw requestFailure(failure);
      markSaved();
      select(data, false, activeSection);
      setRefresh((previous) => previous + 1);
      setNotice('guided.saved');
      return;
    }
    const body = {
      manifestJson: draft?.manifestJson
        ? JSON.stringify({ ...(JSON.parse(draft.manifestJson) as object), displayName: name.trim() })
        : draftManifest(name.trim(), pressure),
      definition: {
        ...(draft?.definition ?? {
          parameter: 'threshold',
          outcomeCode: pressure ? 'PRESSURE_DIFFERENTIAL_HIGH' : 'LOW_FILLAGE',
          title: t(pressure ? 'guided.pressureRecommendationTitle' : 'guided.recommendationTitle'),
          detail: t(pressure ? 'guided.pressureRecommendationDetail' : 'guided.recommendationDetail'),
        }),
        default: Number(threshold),
      },
      sampleCases: activeCases,
      sourceHints: activeHints,
    };
    const { data, error: failure } =
      draft?.id && draft.revision
        ? await editGuidedModuleDraft({ path: { id: draft.id }, body: { ...body, expectedRevision: draft.revision } })
        : await createGuidedModuleDraft({ body });
    if (!data) throw requestFailure(failure);
    markSaved();
    select(data, pressure, activeSection);
    setRefresh((previous) => previous + 1);
    setNotice('guided.saved');
  }

  async function preview() {
    if (!draft?.id || !draft.revision) return;
    const id = draft.id;
    const expectedRevision = draft.revision;
    const results = await Promise.all(
      (pressure
        ? activeCases.map((sample) => ({ pressure: { casingPsi: sample.casingPsi, tubingPsi: sample.tubingPsi } }))
        : activeCases.map((sample) => ({ value: sample.value }))
      ).map((sample) =>
        previewGuidedModuleDraft({
          path: { id },
          body: { expectedRevision, ...sample, parametersJson: '{}' },
        }),
      ),
    );
    setEvidence(
      results.map(({ data }) => {
        if (!data) throw new Error();
        return data;
      }),
    );
  }

  async function submit() {
    if (!draft?.id || !draft.revision) return;
    const { data, error: failure } = await submitGuidedModuleDraft({
      path: { id: draft.id },
      // Saved cases are the evidence of record; readings are posted only for drafts saved before cases existed.
      body: {
        expectedRevision: draft.revision,
        ...(draft.sampleCases?.length
          ? {}
          : pressure
            ? { pressureReadings: activeCases.map(({ casingPsi, tubingPsi }) => ({ casingPsi, tubingPsi })) }
            : { readings: activeCases.map(({ value }) => value ?? null) }),
      },
    });
    if (!data) throw requestFailure(failure);
    setSubmittedReference(draft.id + '/' + draft.revision);
    setReviewKey(draft.id + '/' + draft.revision);
    setNotice('guided.submitted');
  }

  async function openReview() {
    setReview(null);
    setReview(await loadReview(reviewKey));
    setReason('');
    setApproved(false);
  }

  async function approve() {
    if (!review?.draftId || !review.revision || !review.packageSha256) return;
    const { data } = await approveGuidedModuleSubmission({
      path: { id: review.draftId, revision: review.revision },
      body: { packageSha256: review.packageSha256, reason: reason.trim() },
    });
    if (!data?.isEnabled) throw new Error();
    setApproved(true);
    setNotice('guided.approved');
    await onRefresh();
  }

  let pageTitle = t('guided.untitledModule');
  if (showReviewQueue) pageTitle = t('guided.pendingReviews');
  else if (canReview && !canAuthor) pageTitle = t('guided.reviewsTab');
  else if (draft) pageTitle = name.trim() || t('guided.untitledModule');

  return (
    <section className="panel guided-authoring">
      <PanelHeader title={pageTitle} />
      {canAuthor && (draft || contractOnly) && activeProvider && (
        <p className="connection-help">
          {t('guided.providerLabel', {
            provider: providers.find((item) => item.adapterKey === activeProvider)?.displayName ?? activeProvider,
          })}
          {providers.find((item) => item.adapterKey === activeProvider)?.currentValuesOnly
            ? ' · ' + t('guided.providerCurrentOnly')
            : ''}
        </p>
      )}
      {error && <Notice tone="error">{t((errorCode && knownErrorCodes[errorCode]) || 'guided.error')}</Notice>}
      {notice && <Notice tone="success">{t(notice)}</Notice>}
      {canAuthor && (
        <>
          <div className="guided-authoring-editor">
            <div className="guided-authoring-entry">
              <details className="guided-authoring-starters" ref={starterChooser}>
                <summary className="ui-action-button secondary-button">{t('guided.newModule')}</summary>
                <SelectField
                  label={t('guided.chooseProvider')}
                  value={starterProvider}
                  disabled={busy}
                  onChange={(event) => setStarterProvider(event.target.value)}
                >
                  <option value="">{t('guided.noProvider')}</option>
                  {providers.map((item) => (
                    <option key={item.adapterKey} value={item.adapterKey ?? ''}>
                      {item.displayName}
                    </option>
                  ))}
                </SelectField>
                {providers.find((item) => item.adapterKey === starterProvider)?.currentValuesOnly && (
                  <Notice tone="warning" announce={false}>
                    {t('guided.providerCurrentOnly')}
                  </Notice>
                )}
                <div className="guided-authoring-actions">
                  <ActionButton
                    label={t('guided.new')}
                    disabled={busy || starterProvider !== 'foresite'}
                    onClick={() => {
                      changeDraft(null);
                      if (starterChooser.current) starterChooser.current.open = false;
                    }}
                  />
                  <ActionButton
                    label={t('guided.newContract')}
                    disabled={busy || starterProvider === ''}
                    onClick={() => {
                      requestNavigation(() => {
                        select(null);
                        setContractManifest(blankContractManifest('', starterProvider));
                      });
                      if (starterChooser.current) starterChooser.current.open = false;
                    }}
                  />
                  <ActionButton
                    label={t('guided.newPressure')}
                    disabled={busy || starterProvider !== 'cygnet'}
                    onClick={() => {
                      changeDraft(null, true);
                      if (starterChooser.current) starterChooser.current.open = false;
                    }}
                  />
                </div>
              </details>
              <div className="guided-authoring-draft-filter">
                <SelectField
                  label={t('guided.filterProvider')}
                  value={providerFilter}
                  disabled={busy}
                  onChange={(event) => {
                    setProviderFilter(event.target.value);
                    setOffset(0);
                  }}
                >
                  <option value="">{t('guided.allProviders')}</option>
                  {providers.map((item) => (
                    <option key={item.adapterKey} value={item.adapterKey ?? ''}>
                      {item.displayName}
                    </option>
                  ))}
                </SelectField>
              </div>
              <div className="guided-authoring-draft-picker">
                <SelectField
                  label={t('guided.openDraft')}
                  value={draft?.id ?? ''}
                  disabled={busy || (drafts.length === 0 && !draft)}
                  onChange={(event) => {
                    const selected = drafts.find((item) => item.id === event.target.value);
                    if (selected) changeDraft(selected);
                  }}
                >
                  <option value="">{t('guided.chooseDraft')}</option>
                  {draft && !drafts.some((item) => item.id === draft.id) && (
                    <option value={draft.id}>{displayName(draft.manifestJson)}</option>
                  )}
                  {drafts.map((item) => (
                    <option key={item.id} value={item.id}>
                      {displayName(item.manifestJson)}
                      {providerName(item.manifestJson) && ' · ' + providerName(item.manifestJson)}
                    </option>
                  ))}
                </SelectField>
              </div>
              {(offset > 0 || drafts.length === pageSize) && (
                <div className="guided-authoring-actions">
                  {offset > 0 && (
                    <ActionButton
                      label={t('guided.previous')}
                      disabled={busy}
                      onClick={() => setOffset(offset - pageSize)}
                    />
                  )}
                  {drafts.length === pageSize && (
                    <ActionButton
                      label={t('guided.next')}
                      disabled={busy}
                      onClick={() => setOffset(offset + pageSize)}
                    />
                  )}
                </div>
              )}
            </div>
            <div className="guided-authoring-view-bar">
              <SectionTabs<EditorView>
                prefix={editorId}
                panelId={editorId + '-editor'}
                label={t('guided.editorView')}
                selected={view}
                tabs={[
                  { id: 'workflow', label: t('guided.workflow') },
                  { id: 'ide', label: t('guided.ide') },
                ]}
                onSelect={changeView}
              />
              {!inspectOnly && (
                <div className="guided-authoring-actions">
                  <ActionButton
                    label={t('guided.save')}
                    disabled={busy || !valid || !dirty}
                    onClick={() => void run(save)}
                  />
                  {!contractOnly && (
                    <ActionButton
                      label={t('guided.submit')}
                      disabled={
                        busy ||
                        dirty ||
                        Boolean(submittedReference) ||
                        evidence.length !== sampleCount ||
                        !expectationsMet
                      }
                      onClick={() => void run(submit)}
                    />
                  )}
                  {draft?.id && !submittedReference && !confirmDelete && (
                    <ActionButton label={t('guided.delete')} disabled={busy} onClick={() => setConfirmDelete(true)} />
                  )}
                </div>
              )}
              {!inspectOnly && blockedReason && (
                <Notice>{t(blockedReason.key, { port: blockedReason.port ?? '' })}</Notice>
              )}
              {draft?.id && contractOnly && !dirty && !submittedReference && !confirmDelete && (
                <div className="guided-authoring-actions">
                  <SelectField
                    label={t('guided.copyProvider')}
                    value={copyTarget || otherProviders[0]?.adapterKey || ''}
                    disabled={busy || otherProviders.length === 0}
                    onChange={(event) => setCopyTarget(event.target.value)}
                  >
                    {otherProviders.map((item) => (
                      <option key={item.adapterKey} value={item.adapterKey ?? ''}>
                        {item.displayName}
                      </option>
                    ))}
                  </SelectField>
                  <ActionButton
                    label={t('guided.copyContract')}
                    disabled={busy || otherProviders.length === 0}
                    onClick={() => void run(() => copyContract(copyTarget || otherProviders[0]?.adapterKey || ''))}
                  />
                </div>
              )}
              {confirmDelete && draft?.id && (
                <ConfirmPanel title={t('guided.deleteTitle')} level={3} focusOnOpen>
                  <p>{t('guided.deleteConfirm', { name: displayName(draft.manifestJson) })}</p>
                  <FormActions>
                    <ActionButton
                      label={t('guided.deleteNo')}
                      disabled={busy}
                      onClick={() => setConfirmDelete(false)}
                    />
                    <ActionButton label={t('guided.deleteYes')} disabled={busy} onClick={() => void run(deleteDraft)} />
                  </FormActions>
                </ConfirmPanel>
              )}
            </div>
            <div
              className="guided-authoring-workbench"
              data-view={view}
              data-inspect-only={inspectOnly}
              id={editorId + '-editor'}
              role="tabpanel"
              aria-labelledby={editorId + '-tab-' + view}
            >
              {view === 'ide' && !inspectOnly && !contractOnly && (
                <nav className="guided-authoring-outline" aria-label={t('guided.outline')}>
                  <PanelHeader title={t('guided.outline')} level={3} />
                  <ActionButton
                    label={t('guided.moduleDetails')}
                    aria-controls={moduleSectionId}
                    aria-pressed={activeSection === 'module'}
                    onClick={() => setEditorSection('module')}
                  />
                  {!inspectOnly && (
                    <ActionButton
                      label={t('guided.rule')}
                      aria-controls={ruleSectionId}
                      aria-pressed={activeSection === 'rule'}
                      onClick={() => setEditorSection('rule')}
                    />
                  )}
                  {showLivePreview && (
                    <ActionButton
                      label={t('guided.liveData')}
                      aria-controls={liveSectionId}
                      aria-pressed={activeSection === 'live'}
                      onClick={() => setEditorSection('live')}
                    />
                  )}
                </nav>
              )}
              <div className="guided-authoring-main">
                <section id={moduleSectionId} hidden={view === 'ide' && !contractOnly && activeSection !== 'module'}>
                  {!inspectOnly && (
                    <>
                      <PanelHeader title={t('guided.moduleDetails')} level={3} />
                      <div className="guided-authoring-fields">
                        <TextField
                          label={t('guided.name')}
                          maxLength={256}
                          value={name}
                          disabled={busy}
                          onChange={(event) => setName(event.target.value)}
                        />
                      </div>
                    </>
                  )}
                  {inspectOnly && (
                    <Notice tone="info" announce={false}>
                      {t('guided.inspectOnlyContract')}
                    </Notice>
                  )}
                  {contractOnly ? (
                    <>
                      <Notice tone="info" announce={false}>
                        {t('guided.contractOnlyNotice')}
                      </Notice>
                      <GuidedContractEditor
                        key={draft?.id ?? 'new'}
                        manifestJson={contractManifest}
                        disabled={busy}
                        onChange={setContractManifest}
                        hints={activeHints}
                        externalSources={
                          providers.find((item) => item.adapterKey === activeProvider)?.externalSources ?? []
                        }
                        onHintsChange={setHints}
                      />
                      {view === 'ide' && draft?.manifestJson && (
                        <CodeBlock label={t('guided.contractSource')}>
                          {JSON.stringify(parseManifest(draft.manifestJson), null, 2)}
                        </CodeBlock>
                      )}
                    </>
                  ) : (
                    <GuidedModuleContract
                      key={draft?.id ?? 'new'}
                      manifestJson={draft?.manifestJson}
                      initiallyOpen={inspectOnly}
                      showExecutionBoundary={inspectOnly}
                    />
                  )}
                </section>
                {!inspectOnly && !contractOnly && (
                  <section id={ruleSectionId} hidden={view === 'ide' && activeSection !== 'rule'}>
                    <PanelHeader title={t('guided.rule')} level={3} />
                    <p>{t(pressure ? 'guided.pressureRule' : 'guided.fillageRule')}</p>
                    <div className="guided-authoring-fields">
                      <NumberField
                        label={t(pressure ? 'guided.pressureThreshold' : 'guided.threshold')}
                        min={0}
                        max={maximum}
                        step="any"
                        value={threshold}
                        disabled={busy}
                        onChange={(event) => setThreshold(event.target.value)}
                      />
                    </div>
                  </section>
                )}
                {showLivePreview && draft?.id && draft.revision && (
                  <div id={liveSectionId} hidden={view === 'ide' && !contractOnly && activeSection !== 'live'}>
                    <GuidedLivePreview
                      key={draft.id + '/' + draft.revision}
                      draftId={draft.id}
                      revision={draft.revision}
                      canRun={!contractOnly}
                      onUseDailyField={
                        contractOnly && contractManifest
                          ? (quantity, unit) => setContractManifest(addDailyInput(contractManifest, quantity, unit))
                          : undefined
                      }
                      declaredDaily={dailyDeclared}
                      inputs={
                        draft.manifestJson
                          ? (
                              JSON.parse(draft.manifestJson) as {
                                dataRequirements?: { id: string; quantity: string }[];
                              }
                            ).dataRequirements
                          : undefined
                      }
                    />
                  </div>
                )}
              </div>
              {!inspectOnly && !contractOnly && (
                <section className="guided-authoring-inspector" aria-label={t('guided.scenarios')}>
                  <PanelHeader title={t('guided.scenarios')} level={3} />
                  <GuidedSampleCases
                    cases={activeCases}
                    pressure={pressure}
                    outcomeCode={
                      draft?.definition?.outcomeCode ?? (pressure ? 'PRESSURE_DIFFERENTIAL_HIGH' : 'LOW_FILLAGE')
                    }
                    results={dirty ? [] : evidence}
                    disabled={busy}
                    onChange={setCases}
                  />
                  <div className="guided-authoring-actions">
                    <ActionButton
                      label={t('guided.preview')}
                      disabled={busy || !draft || dirty}
                      onClick={() => void run(preview)}
                    />
                  </div>
                  {dirty && evidence.length > 0 && <p>{t('guided.staleScenarios')}</p>}
                  {!dirty &&
                    evidence.length > 0 &&
                    (view === 'workflow' ? (
                      <ul>
                        {evidence.map((item, index) => (
                          <li key={index}>
                            {activeCases[index]?.name} {' · '} {sampleReading(index, item.differentialPsi)} {' · '}{' '}
                            {sampleResult(item)}
                          </li>
                        ))}
                      </ul>
                    ) : (
                      <>
                        <SelectField
                          label={t('guided.scenario')}
                          value={scenario}
                          onChange={(event) => setScenario(Number(event.target.value))}
                        >
                          {evidence.map((_, index) => (
                            <option key={index} value={index}>
                              {t('guided.scenarioNumber', { number: index + 1 })}
                            </option>
                          ))}
                        </SelectField>
                        <p>
                          {sampleReading(scenario, evidence[scenario].differentialPsi)} {' · '}{' '}
                          {sampleResult(evidence[scenario])}
                        </p>
                      </>
                    ))}
                </section>
              )}
            </div>
            {submittedReference && (
              <TextField
                label={t('guided.reviewLink')}
                value={new URL(hashFor('authoring', submittedReference), window.location.href).href}
                readOnly
              />
            )}
          </div>
          <p className="guided-authoring-mobile">{t('guided.desktop')}</p>
        </>
      )}
      {canReview && (
        <section className="guided-authoring-review" aria-label={t('guided.review')}>
          {showReviewQueue ? (
            <GuidedReviewQueue />
          ) : (
            <>
              {initialReviewReference && (
                <ContextLink href={hashFor('authoring', 'reviews')} label={t('guided.backToReviews')} />
              )}
              <TextField
                label={t('guided.reviewReference')}
                value={reviewKey}
                disabled={busy}
                onChange={(event) => {
                  setReviewKey(event.target.value);
                  setReview(null);
                }}
              />
              <ActionButton
                label={t('guided.review')}
                disabled={busy || !reviewKey.trim()}
                onClick={() => void run(openReview)}
              />
            </>
          )}
          {review && (
            <>
              {review.reviewedManifestJson && <h3>{displayName(review.reviewedManifestJson)}</h3>}
              <p>
                {isPressure(review.reviewedManifestJson)
                  ? t('guided.pressureThreshold') + ' ' + review.definition?.default + ' psi'
                  : t('guided.threshold') + ' ' + review.definition?.default + '%'}
              </p>
              <p>{review.definition?.title}</p>
              <ul>
                {review.cases?.map((item, index) => (
                  <li key={index}>
                    {item.pressure ? (
                      <PressureEvidence reading={item.pressure} differential={item.differentialPsi} />
                    ) : item.value == null ? (
                      t('guided.unavailable')
                    ) : (
                      item.value + '%'
                    )}{' '}
                    {' · '}
                    <span title={item.outcomeCode ?? item.failureCode ?? undefined}>
                      {outcomeLabel(item.outcomeCode ?? item.failureCode).text}
                    </span>
                  </li>
                ))}
              </ul>
              {canApprove && (
                <div className="guided-authoring-approval">
                  <TextField
                    label={t('guided.reason')}
                    value={reason}
                    disabled={busy}
                    onChange={(event) => setReason(event.target.value)}
                  />
                  <ActionButton
                    label={t('guided.approve')}
                    disabled={busy || !reason.trim() || approved}
                    onClick={() => void run(approve)}
                  />
                </div>
              )}
            </>
          )}
        </section>
      )}
    </section>
  );
}
