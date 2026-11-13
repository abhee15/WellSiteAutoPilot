import { useEffect, useState } from 'react';
import {
  approveGuidedModuleSubmission,
  createGuidedModuleDraft,
  editGuidedModuleDraft,
  getGuidedModuleSubmission,
  listGuidedModuleDrafts,
  previewGuidedModuleDraft,
  submitGuidedModuleDraft,
} from '../api/generated/v1/sdk.gen';
import type {
  GuidedModuleDraftResponse,
  GuidedModulePreviewResponse,
  GuidedModuleReviewResponse,
  GuidedPressureReadingRequest,
} from '../api/generated/v1/types.gen';
import { ActionButton } from '../components/ActionButton';
import { NumberField, TextField } from '../components/FormFields';
import { Notice } from '../components/Notice';
import { PanelHeader } from '../components/common';
import { t } from '../localization';
import { requestNavigation, useUnsavedChangesGuard } from '../navigationGuard';
import { hashFor, useHashDetail } from '../routing';
import { outcomeLabel } from '../words';
import './guidedAuthoring.css';

const pageSize = 25;

const pressureEntryPoint = 'wsa.declarative.difference.v1';

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
  onRefresh,
}: {
  permissions: readonly string[];
  onRefresh: () => Promise<void>;
}) {
  const [reviewReference] = useHashDetail('authoring');
  return (
    <GuidedAuthoringPage
      key={reviewReference}
      initialReviewReference={reviewReference ?? ''}
      canAuthor={permissions.includes('logic.authoring.manage')}
      canReview={permissions.includes('logic.authoring.review')}
      canApprove={permissions.includes('logic.manage')}
      onRefresh={onRefresh}
    />
  );
}

export function GuidedAuthoringPage({
  canAuthor,
  canReview,
  canApprove,
  initialReviewReference = '',
  onRefresh,
}: {
  canAuthor: boolean;
  canReview: boolean;
  canApprove: boolean;
  initialReviewReference?: string;
  onRefresh: () => Promise<void>;
}) {
  const [drafts, setDrafts] = useState<GuidedModuleDraftResponse[]>([]);
  const [offset, setOffset] = useState(0);
  const [refresh, setRefresh] = useState(0);
  const [draft, setDraft] = useState<GuidedModuleDraftResponse | null>(null);
  const [pressure, setPressure] = useState(false);
  const [name, setName] = useState('');
  const [threshold, setThreshold] = useState('70');
  const [evidence, setEvidence] = useState<GuidedModulePreviewResponse[]>([]);
  const [reviewKey, setReviewKey] = useState(initialReviewReference);
  const [submittedReference, setSubmittedReference] = useState('');
  const [review, setReview] = useState<GuidedModuleReviewResponse | null>(null);
  const [reason, setReason] = useState('');
  const [approved, setApproved] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(false);
  const [notice, setNotice] = useState<'guided.saved' | 'guided.submitted' | 'guided.approved' | null>(null);

  useEffect(() => {
    if (!canAuthor) return;
    const controller = new AbortController();
    void listGuidedModuleDrafts({ query: { offset, limit: pageSize }, signal: controller.signal })
      .then(({ data }) => {
        if (controller.signal.aborted) return;
        if (data) setDrafts(data);
        else setError(true);
      })
      .catch(() => {
        if (!controller.signal.aborted) setError(true);
      });
    return () => controller.abort();
  }, [canAuthor, offset, refresh]);

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

  function select(next: GuidedModuleDraftResponse | null, nextPressure = Boolean(isPressure(next?.manifestJson))) {
    setPressure(nextPressure);
    setDraft(next);
    setName(displayName(next?.manifestJson));
    setThreshold(String(next?.definition?.default ?? (nextPressure ? 150 : 70)));
    setEvidence([]);
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
    } catch {
      setError(true);
    } finally {
      setBusy(false);
    }
  }

  const boundary = draft?.definition?.default ?? (pressure ? 150 : 70);
  const maximum = pressure ? 10000 : 100;
  const readings = [boundary === 0 ? 8 : Math.max(0, boundary - 8), boundary, null] as const;
  const pressureReadings: GuidedPressureReadingRequest[] = [
    { casingPsi: 120, tubingPsi: 120 },
    { casingPsi: boundary, tubingPsi: 0 },
    { casingPsi: boundary + 1, tubingPsi: 0 },
    { casingPsi: null, tubingPsi: 120 },
  ];
  const sampleCount = pressure ? pressureReadings.length : readings.length;
  const valid =
    name.trim().length > 0 && threshold.trim() !== '' && Number(threshold) >= 0 && Number(threshold) <= maximum;
  const dirty = name !== displayName(draft?.manifestJson) || threshold !== String(boundary);
  const markSaved = useUnsavedChangesGuard(canAuthor && dirty);

  function changeDraft(next: GuidedModuleDraftResponse | null, nextPressure?: boolean) {
    requestNavigation(() => select(next, nextPressure));
  }

  async function save() {
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
    };
    const { data } =
      draft?.id && draft.revision
        ? await editGuidedModuleDraft({ path: { id: draft.id }, body: { ...body, expectedRevision: draft.revision } })
        : await createGuidedModuleDraft({ body });
    if (!data) throw new Error();
    markSaved();
    select(data);
    setRefresh((previous) => previous + 1);
    setNotice('guided.saved');
  }

  async function preview() {
    if (!draft?.id || !draft.revision) return;
    const id = draft.id;
    const expectedRevision = draft.revision;
    const results = await Promise.all(
      (pressure ? pressureReadings.map((sample) => ({ pressure: sample })) : readings.map((value) => ({ value }))).map(
        (sample) =>
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
    const { data } = await submitGuidedModuleDraft({
      path: { id: draft.id },
      body: { expectedRevision: draft.revision, ...(pressure ? { pressureReadings } : { readings: [...readings] }) },
    });
    if (!data) throw new Error();
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

  return (
    <section className="panel guided-authoring">
      <PanelHeader title={t('guided.heading')} />
      {error && <Notice tone="error">{t('guided.error')}</Notice>}
      {notice && <Notice tone="success">{t(notice)}</Notice>}
      {canAuthor && (
        <>
          <div className="guided-authoring-editor">
            <div className="guided-authoring-actions">
              <ActionButton label={t('guided.new')} disabled={busy} onClick={() => changeDraft(null)} />
              <ActionButton label={t('guided.newPressure')} disabled={busy} onClick={() => changeDraft(null, true)} />
              {drafts.map((item) => (
                <ActionButton
                  key={item.id}
                  label={displayName(item.manifestJson)}
                  disabled={busy}
                  onClick={() => changeDraft(item)}
                />
              ))}
              {offset > 0 && (
                <ActionButton
                  label={t('guided.previous')}
                  disabled={busy}
                  onClick={() => setOffset(offset - pageSize)}
                />
              )}
              {drafts.length === pageSize && (
                <ActionButton label={t('guided.next')} disabled={busy} onClick={() => setOffset(offset + pageSize)} />
              )}
            </div>
            {pressure && <p>{t('guided.pressureRule')}</p>}
            <div className="guided-authoring-fields">
              <TextField
                label={t('guided.name')}
                maxLength={256}
                value={name}
                disabled={busy}
                onChange={(event) => setName(event.target.value)}
              />
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
            <div className="guided-authoring-actions">
              <ActionButton
                label={t('guided.save')}
                disabled={busy || !valid || !dirty}
                onClick={() => void run(save)}
              />
              <ActionButton
                label={t('guided.preview')}
                disabled={busy || !draft || dirty}
                onClick={() => void run(preview)}
              />
              <ActionButton
                label={t('guided.submit')}
                disabled={busy || dirty || Boolean(submittedReference) || evidence.length !== sampleCount}
                onClick={() => void run(submit)}
              />
            </div>
            {!dirty && evidence.length > 0 && (
              <ul>
                {evidence.map((item, index) => (
                  <li key={index}>
                    {pressure ? (
                      <PressureEvidence reading={pressureReadings[index]} differential={item.differentialPsi} />
                    ) : readings[index] === null ? (
                      t('guided.unavailable')
                    ) : (
                      readings[index] + '%'
                    )}{' '}
                    {' · '}
                    <span title={item.outcomeCode ?? item.failureCode ?? undefined}>
                      {item.recommendationTitle ?? outcomeLabel(item.outcomeCode ?? item.failureCode).text}
                    </span>
                  </li>
                ))}
              </ul>
            )}
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
