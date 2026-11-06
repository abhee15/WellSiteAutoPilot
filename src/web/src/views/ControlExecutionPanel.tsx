import { useEffect, useRef, useState } from 'react';
import { ActionButton } from '../components/ActionButton';
import { CheckboxField, ConfirmPanel, FormActions } from '../components/FormFields';
import { Notice } from '../components/Notice';
import type { ConfiguredLogicResponse, ControlActionResponse, RecommendationSummaryResponse } from '../api/generated/v1/types.gen';
import { t } from '../localization';
import './decisionBrief.css';
import { formatDetailTimestamp } from './executionDetail';
import { proposalSentence } from './decisionBrief';
import {
  ControlExecutionError,
  executeApprovedRecommendation,
  gateMessageKey,
} from './controlExecution';
import type { ControlExecutionErrorKind } from './controlExecution';

type Props = {
  item: RecommendationSummaryResponse;
  onCancel: () => void;
  /** The server accepted the execution; the action says what became of the command. */
  onExecuted: (action: ControlActionResponse) => void;
  /** The recommendation is no longer executable by this person; the list should reload. */
  onConflict: () => void;
  assetName?: string;
  configuredLogic?: readonly ConfiguredLogicResponse[];
};

/**
 * Executing is a separate, explicit step from approving. The command comes from the stored, approved
 * recommendation, never from this form, so the panel only asks the person to confirm what they are about to send.
 */
export function ControlExecutionPanel({
  item, onCancel, onExecuted, onConflict, assetName, configuredLogic = [],
}: Props) {
  const logic = configuredLogic.find(candidate => candidate.id === item.configuredLogicId);
  const [acknowledged, setAcknowledged] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ControlExecutionErrorKind | null>(null);
  const [refusal, setRefusal] = useState<string | null>(null);
  const requestRef = useRef<AbortController | null>(null);

  useEffect(() => () => {
    requestRef.current?.abort();
    requestRef.current = null;
  }, []);

  const submit = async () => {
    if (submitting || !acknowledged) return;
    requestRef.current?.abort();
    const controller = new AbortController();
    requestRef.current = controller;
    setSubmitting(true);
    setError(null);
    setRefusal(null);

    try {
      const result = await executeApprovedRecommendation(item.id ?? '', controller.signal);
      if (controller.signal.aborted) return;
      if (result.action) {
        onExecuted(result.action);
      } else {
        // The gate refused: nothing was sent and the recommendation is still approved, so stay here and say why.
        const key = gateMessageKey(result.refusalCode);
        setRefusal(key
          ? t('control.execute.refused', { reason: t(key) })
          : t('control.execute.refusedUnknown', { code: result.refusalCode ?? '' }));
        setAcknowledged(false);
      }
    } catch (failure) {
      if (controller.signal.aborted) return;
      const kind = failure instanceof ControlExecutionError ? failure.kind : 'unavailable';
      setError(kind);
      if (kind === 'conflict' || kind === 'notFound' || kind === 'accessDenied') onConflict();
    } finally {
      if (requestRef.current === controller) {
        requestRef.current = null;
        setSubmitting(false);
      }
    }
  };

  const errors: Record<ControlExecutionErrorKind, string> = {
    invalid: t('control.execute.invalid'),
    accessDenied: t('control.execute.accessDenied'),
    notFound: t('control.execute.notFound'),
    conflict: t('control.execute.conflict'),
    unavailable: t('control.execute.unavailable'),
    invalidResponse: t('control.execute.invalidResponse'),
  };

  return (
    <section id="control-execution" className="execution-detail"
      role="region" aria-label={t('control.execute.heading')}>
      <div className="execution-detail-header">
        <div>
          <h3>{t('control.execute.heading')}</h3>
          <p>{t('control.execute.untilConfirmed')}</p>
        </div>
        <div className="execution-detail-actions">
          <ActionButton label={t('decision.cancel')} onClick={onCancel} disabled={submitting} />
        </div>
      </div>
      <div className="decision-brief">
        <p className="decision-brief-proposal">{proposalSentence(item, assetName)}</p>
        <ul>
          {item.assetId && <li><a href={'#assets/' + item.assetId}>{assetName ?? t('decision.asset')}</a></li>}
          {logic && <li>{t('decision.fromLogic', { name: logic.name ?? '—',
            revision: logic.revisions?.find(candidate => candidate.id === item.configurationRevisionId)?.revisionNumber ?? '—' })}</li>}
          {item.executionId && <li><a href={'#operations/' + item.executionId}>{t('decision.fromRun')}</a></li>}
        </ul>
      </div>
      <dl className="execution-detail-grid">
        <div><dt>{t('decision.asset')}</dt><dd title={item.assetId ?? undefined}>{assetName ?? item.assetId ?? '—'}</dd></div>
        <div><dt>{t('decision.command')}</dt><dd>{item.command ?? item.code ?? '—'}</dd></div>
        <div><dt>{t('decision.suggestedValue')}</dt>
          <dd>{item.suggestedValue ?? '—'} {item.unit ?? ''}</dd></div>
        <div><dt>{t('control.execute.approvedBy')}</dt>
          <dd>{item.decisionBy ?? '—'}<small>{formatDetailTimestamp(item.decisionAtUtc)}</small></dd></div>
      </dl>
      <ConfirmPanel title={t('form.confirmExecution')}>
        <Notice tone="warning" announce={false}>{t('control.execute.notice')}</Notice>
        <CheckboxField label={t('control.execute.confirmation')} checked={acknowledged} disabled={submitting} required
          onChange={event => setAcknowledged(event.target.checked)} />
        {refusal && <Notice tone="error">{refusal}</Notice>}
        {error && <Notice tone="error">{errors[error]}</Notice>}
        {submitting && <Notice>{t('control.execute.submitting')}</Notice>}
        <FormActions>
          <ActionButton variant="primary" label={t('control.execute.submit')}
            disabled={submitting || !acknowledged} busy={submitting} onClick={() => void submit()} />
        </FormActions>
      </ConfirmPanel>
    </section>
  );
}
