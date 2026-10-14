import { useEffect, useRef, useState } from 'react';
import type { RecommendationSummaryResponse } from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { formatDetailTimestamp } from './executionDetail';
import { recommendationIsAwaiting } from './recommendationInbox';
import {
  RecommendationDecisionError,
  submitRecommendationDecision,
} from './recommendationDecision';
import type {
  RecommendationDecision,
  RecommendationDecisionErrorKind,
} from './recommendationDecision';

type Props = {
  item: RecommendationSummaryResponse;
  onCancel: () => void;
  onSaved: () => void;
  onConflict: () => void;
};

export function RecommendationDecisionReviewPanel({
  item, onCancel, onSaved, onConflict,
}: Props) {
  const [choice, setChoice] = useState<RecommendationDecision | null>(null);
  const [reason, setReason] = useState('');
  const [acknowledged, setAcknowledged] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<RecommendationDecisionErrorKind | 'expired' | null>(null);
  const requestRef = useRef<AbortController | null>(null);

  useEffect(() => () => {
    requestRef.current?.abort();
    requestRef.current = null;
  }, []);

  const canSubmit = !submitting && choice !== null && acknowledged &&
    reason.trim().length >= 2 && reason.trim().length <= 1024 &&
    recommendationIsAwaiting(item, Date.now());

  const submit = async () => {
    if (!choice || !canSubmit) {
      setError(recommendationIsAwaiting(item, Date.now()) ? 'invalid' : 'expired');
      return;
    }

    requestRef.current?.abort();
    const controller = new AbortController();
    requestRef.current = controller;
    setSubmitting(true);
    setError(null);

    try {
      await submitRecommendationDecision(item.id ?? '', choice, reason,
        controller.signal);
      if (!controller.signal.aborted) onSaved();
    } catch (failure) {
      if (controller.signal.aborted) return;
      const kind = failure instanceof RecommendationDecisionError
        ? failure.kind : 'unavailable';
      setError(kind);
      if (kind === 'conflict' || kind === 'notFound' || kind === 'accessDenied') {
        onConflict();
      }
    } finally {
      if (requestRef.current === controller) {
        requestRef.current = null;
        setSubmitting(false);
      }
    }
  };

  const errors: Record<RecommendationDecisionErrorKind | 'expired', string> = {
    invalid: t('decision.invalid'),
    accessDenied: t('decision.accessDenied'),
    notFound: t('decision.notFound'),
    conflict: t('decision.conflict'),
    unavailable: t('decision.unavailable'),
    invalidResponse: t('decision.invalidResponse'),
    expired: t('decision.expired'),
  };

  return (
    <section id="recommendation-review" className="execution-detail"
      role="region" aria-label={t('decision.heading')}>
      <div className="execution-detail-header">
        <div>
          <h3>{t('decision.heading')}</h3>
          <p>{t('decision.readOnlyUntilConfirmed')}</p>
        </div>
        <div className="execution-detail-actions">
          <button type="button" onClick={onCancel} disabled={submitting}>
            {t('decision.cancel')}
          </button>
        </div>
      </div>
      <dl className="execution-detail-grid">
        <div><dt>{t('decision.asset')}</dt><dd>{item.assetId ?? '—'}</dd></div>
        <div><dt>{t('decision.command')}</dt><dd>{item.command ?? item.code ?? '—'}</dd></div>
        <div><dt>{t('decision.suggestedValue')}</dt>
          <dd>{item.suggestedValue ?? '—'} {item.unit ?? ''}</dd></div>
        <div><dt>{t('decision.created')}</dt>
          <dd>{formatDetailTimestamp(item.createdAtUtc)}</dd></div>
        <div><dt>{t('decision.expires')}</dt>
          <dd>{formatDetailTimestamp(item.expiresAtUtc)}</dd></div>
        <div><dt>{t('decision.reasonCode')}</dt>
          <dd>{item.reasonCode ?? '—'}</dd></div>
      </dl>
      <p className="recommendation-review-warning" role="note">
        {t('decision.governanceNotice')}
      </p>
      <fieldset className="recommendation-review-options" disabled={submitting}>
        <legend>{t('decision.chooseAction')}</legend>
        <label>
          <input type="radio" name="recommendation-decision" value="Approved"
            checked={choice === 'Approved'}
            onChange={() => { setChoice('Approved'); setError(null); }} />
          {t('decision.approve')}
        </label>
        <label>
          <input type="radio" name="recommendation-decision" value="Rejected"
            checked={choice === 'Rejected'}
            onChange={() => { setChoice('Rejected'); setError(null); }} />
          {t('decision.reject')}
        </label>
      </fieldset>
      <label className="recommendation-review-reason">
        <span>{t('decision.reason')}</span>
        <textarea rows={3} maxLength={1024} value={reason} disabled={submitting}
          onChange={event => { setReason(event.target.value); setError(null); }} />
      </label>
      <label className="recommendation-review-confirm">
        <input type="checkbox" checked={acknowledged} disabled={submitting}
          onChange={event => setAcknowledged(event.target.checked)} />
        {t('decision.confirmation')}
      </label>
      {error && <p className="execution-detail-error" role="alert">{errors[error]}</p>}
      {submitting && <p role="status">{t('decision.submitting')}</p>}
      <div className="execution-detail-actions">
        <button type="button" disabled={!canSubmit} onClick={() => void submit()}>
          {t('decision.submit')}
        </button>
      </div>
    </section>
  );
}
