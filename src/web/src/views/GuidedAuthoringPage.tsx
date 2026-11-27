import { useEffect, useState } from 'react';
import { approveGuidedModuleSubmission, getGuidedModuleSubmission } from '../api/generated/v1/sdk.gen';
import type { GuidedModuleReviewResponse, GuidedPressureReadingRequest } from '../api/generated/v1/types.gen';
import { ActionButton, ActionLink } from '../components/ActionButton';
import { ContextLink } from '../components/ContextLink';
import { PageTabNav } from '../components/PageTabNav';
import { GuidedReviewQueue } from './GuidedReviewQueue';
import { AuthoringFlow } from './AuthoringFlow';
import { AuthoringDrafts } from './AuthoringDrafts';
import { RulesReview } from './AuthoringReview';
import { TextField } from '../components/FormFields';
import { Notice } from '../components/Notice';
import { PanelHeader } from '../components/common';
import { t } from '../localization';
import { hashFor, useHashDetail } from '../routing';
import { outcomeLabel } from '../words';
import './guidedAuthoring.css';

const pressureEntryPoint = 'wsa.declarative.difference.v1';

function isPressure(manifestJson: string | null | undefined) {
  return manifestJson && (JSON.parse(manifestJson) as { entryPoint?: string }).entryPoint === pressureEntryPoint;
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
  const [detail] = useHashDetail('authoring');
  const canAuthor = permissions.includes('logic.authoring.manage');
  const canReview = permissions.includes('logic.authoring.review');
  const showReviews = canReview && (Boolean(detail) || !canAuthor);
  const reviewReference = showReviews && detail !== 'reviews' ? (detail ?? '') : '';
  if (canAuthor && detail === 'new') return <AuthoringFlow />;
  if (canAuthor && detail?.startsWith('draft:'))
    return <AuthoringFlow key={detail} draftId={detail.slice('draft:'.length)} />;
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
      {canAuthor && !showReviews && (
        <>
          <ActionLink href={hashFor('authoring', 'new')} label={t('flow.startModule')} variant="primary" />
          <AuthoringDrafts />
        </>
      )}
      {showReviews && (
        <ModuleReview
          key={reviewReference || 'reviews'}
          initialReviewReference={reviewReference}
          canApprove={permissions.includes('logic.manage')}
          onRefresh={onRefresh}
        />
      )}
    </>
  );
}

export function ModuleReview({
  canApprove,
  initialReviewReference = '',
  onRefresh,
}: {
  canApprove: boolean;
  initialReviewReference?: string;
  onRefresh: () => Promise<void>;
}) {
  const showReviewQueue = !initialReviewReference;
  const [reviewKey, setReviewKey] = useState(initialReviewReference);
  const [review, setReview] = useState<GuidedModuleReviewResponse | null>(null);
  const [reason, setReason] = useState('');
  const [approved, setApproved] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(false);

  useEffect(() => {
    if (!initialReviewReference) return;
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
  }, [initialReviewReference]);

  async function run(action: () => Promise<void>) {
    setBusy(true);
    setError(false);
    setApproved(false);
    try {
      await action();
    } catch {
      setError(true);
    } finally {
      setBusy(false);
    }
  }

  async function openReview() {
    setReview(null);
    setReview(await loadReview(reviewKey));
    setReason('');
  }

  async function approve() {
    if (!review?.draftId || !review.revision || !review.packageSha256) return;
    const { data } = await approveGuidedModuleSubmission({
      path: { id: review.draftId, revision: review.revision },
      body: { packageSha256: review.packageSha256, reason: reason.trim() },
    });
    if (!data?.isEnabled) throw new Error();
    setApproved(true);
    await onRefresh();
  }

  return (
    <section className="panel guided-authoring">
      <PanelHeader title={showReviewQueue ? t('guided.pendingReviews') : t('guided.reviewsTab')} />
      {error && <Notice tone="error">{t('guided.error')}</Notice>}
      {approved && <Notice tone="success">{t('guided.approved')}</Notice>}
      <section className="guided-authoring-review" aria-label={t('guided.review')}>
        {showReviewQueue ? (
          <GuidedReviewQueue />
        ) : (
          <>
            <ContextLink href={hashFor('authoring', 'reviews')} label={t('guided.backToReviews')} />
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
            {review.rulesJson ? (
              <RulesReview rulesJson={review.rulesJson} evidenceJson={review.rulesEvidenceJson} />
            ) : (
              <>
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
              </>
            )}
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
    </section>
  );
}
