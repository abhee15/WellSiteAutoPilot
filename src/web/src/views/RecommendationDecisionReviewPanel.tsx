import { ContextLink } from '../components/ContextLink';
import { useEffect, useRef, useState } from 'react';
import { ActionButton } from '../components/ActionButton';
import { CheckboxField, ConfirmPanel, FormActions, RadioGroup, TextAreaField } from '../components/FormFields';
import { Notice } from '../components/Notice';
import type { ConfiguredLogicResponse, RecommendationSummaryResponse } from '../api/generated/v1/types.gen';
import { t } from '../localization';
import './decisionBrief.css';
import { humanize } from './auditPresentation';
import { formatDetailTimestamp } from './executionDetail';
import { recommendationIsAwaiting } from './recommendationInbox';
import { proposalSentence, timeLeft } from './decisionBrief';
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
  assetName?: string;
  /** Used to name the logic and revision behind the proposal, when loaded and readable. */
  configuredLogic?: readonly ConfiguredLogicResponse[];
};

export function RecommendationDecisionReviewPanel({
  item, onCancel, onSaved, onConflict, assetName, configuredLogic = [],
}: Props) {
  const logic = configuredLogic.find(candidate => candidate.id === item.configuredLogicId);
  const revision = logic?.revisions?.find(candidate => candidate.id === item.configurationRevisionId);
  const left = timeLeft(item.expiresAtUtc, Date.now());
  const [choice, setChoice] = useState<RecommendationDecision | null>(null);
  const [reason, setReason] = useState('');
  const [reasonTouched, setReasonTouched] = useState(false);
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
        </div>
        <div className="execution-detail-actions">
          <ActionButton label={t('decision.cancel')} onClick={onCancel} disabled={submitting} />
        </div>
      </div>
      <div className="decision-brief">
        <p className="decision-brief-proposal">{proposalSentence(item, assetName)}</p>
        <ul>
          {item.assetId && <li><ContextLink appearance="record" href={'#assets/' + item.assetId} label={assetName ?? t('decision.asset')} /></li>}
          <li>{logic ? t('decision.fromLogic', { name: logic.name ?? '—',
            revision: revision?.revisionNumber ?? '—' }) : t('workspace.fromModule', { name: item.moduleId ?? '—', version: item.moduleVersion ?? '—' })}</li>
          {item.executionId && <li><ContextLink href={'#operations/' + item.executionId} label={t('decision.fromRun')} /></li>}
          {left && <li>{left}</li>}
        </ul>
        {item.reasonCode && <p>{t('inbox.reason')}: {humanize(item.reasonCode)}</p>}
      </div>
      <details className="decision-metadata">
      <summary>{t('workspace.proposalDetails')}</summary>
      <dl className="execution-detail-grid">
        <div><dt>{t('decision.asset')}</dt><dd title={item.assetId ?? undefined}>{assetName ?? item.assetId ?? '—'}</dd></div>
        <div><dt>{t('decision.command')}</dt><dd>{item.command ?? item.code ?? '—'}</dd></div>
        <div><dt>{t('decision.suggestedValue')}</dt>
          <dd>{item.suggestedValue ?? '—'} {item.unit ?? ''}</dd></div>
        <div><dt>{t('decision.created')}</dt>
          <dd>{formatDetailTimestamp(item.createdAtUtc)}</dd></div>
        <div><dt>{t('decision.expires')}</dt>
          <dd>{formatDetailTimestamp(item.expiresAtUtc)}</dd></div>
        <div><dt>{t('decision.reasonCode')}</dt>
          <dd title={item.reasonCode ?? undefined}>{item.reasonCode ? humanize(item.reasonCode) : '—'}</dd></div>
      </dl>
      </details>
      <div className="decision-form">
        <Notice announce={false}>{t('decision.recordedNotice')}</Notice>
        <RadioGroup label={t('decision.chooseAction')} required
          disabled={submitting} value={choice}
          options={[{ value: 'Approved', label: t('decision.approve') }, { value: 'Rejected', label: t('decision.reject') }]}
          onChange={value => {
            if (value === 'Approved' || value === 'Rejected') setChoice(value);
            setError(null);
          }} />
        <TextAreaField label={t('decision.reason')} rows={3} minLength={2} maxLength={1024} required
          value={reason} disabled={submitting} helper={t('form.reasonHelp')}
          error={reasonTouched && reason.trim().length < 2 ? t('form.reasonShort') : undefined}
          onBlur={() => setReasonTouched(true)}
          onChange={event => { setReason(event.target.value); setError(null); }} />
        <ConfirmPanel title={t('form.confirmDecision')}>
          <CheckboxField label={t('decision.confirmation')} checked={acknowledged} disabled={submitting} required
            onChange={event => setAcknowledged(event.target.checked)} />
          {error && <Notice tone="error">{errors[error]}</Notice>}
          {submitting && <Notice>{t('decision.submitting')}</Notice>}
          <FormActions>
            <ActionButton variant="primary" label={t('decision.submit')} disabled={!canSubmit} busy={submitting} onClick={() => void submit()} />
          </FormActions>
        </ConfirmPanel>
      </div>
    </section>
  );
}
