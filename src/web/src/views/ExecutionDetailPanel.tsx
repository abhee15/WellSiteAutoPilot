import { useEffect, useState } from 'react';
import type { ExecutionResponse } from '../api/generated/v1/types.gen';
import { t } from '../localization';
import {
  ExecutionDetailError,
  fetchExecutionDetail,
  formatDetailTimestamp,
  previewExecutionOutput,
} from './executionDetail';
import type { ExecutionDetailErrorKind } from './executionDetail';

type Props = {
  executionId: string;
  onClose: () => void;
};

export function ExecutionDetailPanel({ executionId, onClose }: Props) {
  const [detail, setDetail] = useState<ExecutionResponse | null>(null);
  const [error, setError] = useState<ExecutionDetailErrorKind | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [refreshNumber, setRefreshNumber] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    let active = true;
    setDetail(null);
    setError(null);
    setIsLoading(true);

    void fetchExecutionDetail(executionId, controller.signal)
      .then(value => {
        if (active && !controller.signal.aborted) {
          setDetail(value);
          setIsLoading(false);
        }
      })
      .catch(reason => {
        if (!active || controller.signal.aborted) return;
        setDetail(null);
        setError(reason instanceof ExecutionDetailError ? reason.kind : 'unavailable');
        setIsLoading(false);
      });

    // Closing, changing selection, or changing the session unmounts this
    // component, revoking its old data and cancelling its in-flight request.
    return () => {
      active = false;
      controller.abort();
    };
  }, [executionId, refreshNumber]);

  const errorMessages: Record<ExecutionDetailErrorKind, string> = {
    invalidRequest: t('execution.invalidRequest'),
    accessDenied: t('execution.accessDenied'),
    notFound: t('execution.notFound'),
    unavailable: t('execution.unavailable'),
    invalidResponse: t('execution.invalidResponse'),
  };


  return (
    <section id="execution-details" className="execution-detail" role="region"
      aria-label={t('execution.heading')}>
      <div className="execution-detail-header">
        <div>
          <h3>{t('execution.heading')}</h3>
          <p>{t('execution.readOnly')}</p>
        </div>
        <div className="execution-detail-actions">
          <button type="button" disabled={isLoading}
            onClick={() => setRefreshNumber(value => value + 1)}>
            {t('execution.refresh')}
          </button>
          <button type="button" onClick={onClose}>{t('execution.close')}</button>
        </div>
      </div>
      {isLoading && <p role="status">{t('execution.loading')}</p>}
      {error && <p role="alert" className="execution-detail-error">{errorMessages[error]}</p>}
      {detail && <ExecutionDetailContent detail={detail} />}
    </section>
  );
}

/** Pure view to keep response rendering testable independently of network effects. */
export function ExecutionDetailContent({ detail }: { detail: ExecutionResponse }) {
  const fields: Array<[string, string | null | undefined]> = [
    [t('execution.status'), detail.status],
    [t('execution.mode'), detail.mode],
    [t('execution.trigger'), detail.trigger],
    [t('execution.module'), [detail.moduleId, detail.moduleVersion]
      .filter(Boolean).join(' · ')],
    [t('execution.logicInstance'), detail.logicInstanceId],
    [t('execution.revision'), detail.configurationRevisionId],
    [t('execution.asset'), detail.assetExternalId ?? detail.assetId],
    [t('execution.quantity'), detail.quantity],
    [t('execution.contract'), String(detail.requestContractVersion ?? '—')],
    [t('execution.requested'), formatDetailTimestamp(detail.requestedAtUtc)],
    [t('execution.started'), formatDetailTimestamp(detail.startedAtUtc)],
    [t('execution.completed'), formatDetailTimestamp(detail.completedAtUtc)],
    [t('execution.scheduled'), formatDetailTimestamp(detail.scheduledForUtc)],
    [t('execution.result'), detail.resultCode],
    [t('execution.failure'), detail.failureCode],
    [t('execution.correlation'), detail.correlationId],
    [t('execution.id'), detail.executionId],
  ];
  const output = previewExecutionOutput(detail.outputJson);

  return (
    <>
      <dl className="execution-detail-grid">
        {fields.map(([label, value]) => (
          <div key={label}>
            <dt>{label}</dt>
            <dd>{value || '—'}</dd>
          </div>
        ))}
      </dl>
      {output.text && (
        <details className="execution-detail-output">
          <summary>{t('execution.output')}</summary>
          <pre>{output.text}</pre>
          {output.truncated && <p>{t('execution.truncated')}</p>}
        </details>
      )}
    </>
  );
}
