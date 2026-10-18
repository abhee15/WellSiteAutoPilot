import { useEffect, useRef, useState } from 'react';
import type { AssetResponse, ConfiguredLogicResponse, ExecutionResponse } from '../api/generated/v1/types.gen';
import { hashFor } from '../routing';
import { modeSentence, outcomeLabel, statusLabel, triggerLabel } from '../words';
import { describeRunContext } from './runContext';
import { t } from '../localization';
import {
  ExecutionDetailError,
  fetchExecutionDetail,
  formatDetailTimestamp,
  IN_FLIGHT_EXECUTION_STATUSES,
  previewExecutionOutput,
  executionInputObservations,
} from './executionDetail';
import type { ExecutionDetailErrorKind } from './executionDetail';
import './executionSummary.css';

type Props = {
  executionId: string;
  onClose: () => void;
  poll?: boolean;
  onObserved?: (detail: ExecutionResponse) => void;
  onAccessLost?: () => void;
  configuredLogic?: readonly ConfiguredLogicResponse[];
  assets?: readonly AssetResponse[];
};

export function ExecutionDetailPanel({ executionId, onClose, poll, onObserved, onAccessLost,
  configuredLogic = [], assets = [] }: Props) {
  const [detail, setDetail] = useState<ExecutionResponse | null>(null);
  const [error, setError] = useState<ExecutionDetailErrorKind | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [refreshNumber, setRefreshNumber] = useState(0);

  const callbacks = useRef({ onObserved, onAccessLost });
  useEffect(() => { callbacks.current = { onObserved, onAccessLost }; }, [onObserved, onAccessLost]);
  useEffect(() => {
    if (!poll || !detail || !IN_FLIGHT_EXECUTION_STATUSES.includes(detail.status ?? '')) return;
    const timer = window.setTimeout(() => setRefreshNumber(n => n + 1), 2000);
    return () => window.clearTimeout(timer);
  }, [detail, poll]);

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
          callbacks.current.onObserved?.(value);
        }
      })
      .catch(reason => {
        if (!active || controller.signal.aborted) return;
        if (reason instanceof ExecutionDetailError && ['accessDenied', 'notFound'].includes(reason.kind))
          callbacks.current.onAccessLost?.();
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
      {detail && <ExecutionDetailContent detail={detail} configuredLogic={configuredLogic} assets={assets} />}
    </section>
  );
}

/** Pure view to keep response rendering testable independently of network effects. */
export function ExecutionDetailContent({ detail, configuredLogic = [], assets = [] }: {
  detail: ExecutionResponse;
  configuredLogic?: readonly ConfiguredLogicResponse[];
  assets?: readonly AssetResponse[];
}) {
  const context = describeRunContext(detail, configuredLogic, assets);
  const outcome = outcomeLabel(detail.resultCode ?? detail.failureCode);
  const fields: Array<[string, string | null | undefined]> = [
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
  const observations = executionInputObservations(detail.outputJson);

  return (
    <>
      <div className="execution-summary" aria-label={t('execution.summary')}>
        <p className="execution-summary-lead">
          <strong>{statusLabel(detail.status)}</strong>
          {outcome.text && <> · <span title={outcome.code ?? undefined}>{outcome.text}</span></>}
        </p>
        <ul className="execution-summary-list">
          <li>{t('execution.summaryLogic')}{' '}
            {detail.logicInstanceId && context.logicName
              ? <a href={hashFor('logic', detail.logicInstanceId)}>{context.logicName}</a>
              : (detail.moduleId ?? '—')}
            {context.revisionNumber ? ` · ${t('setup.revision', { number: context.revisionNumber })}` : ''}
          </li>
          {context.assetIds.length > 0 && <li>{t('execution.summaryAssets')}{' '}
            {context.assetIds.map((id, index) => <span key={id}>
              {index > 0 && ', '}<a href={hashFor('assets', id)}>{context.assetLabels[index]}</a>
            </span>)}
          </li>}
          <li>{triggerLabel(detail.trigger)}</li>
          {detail.mode && <li>{modeSentence(detail.mode)}</li>}
        </ul>
      </div>
      <dl className="execution-detail-grid">
        {fields.map(([label, value]) => (
          <div key={label}>
            <dt>{label}</dt>
            <dd>{value || '—'}</dd>
          </div>
        ))}
      </dl>
      {observations.length > 0 && <section aria-label={t('shadow.observations')}>
        <h4>{t('shadow.observations')}</h4>
        <div className="table-wrap"><table className="activity-table"><thead><tr>
          {[t('shadow.input'), t('shadow.value'), t('shadow.quality'), t('shadow.timestamp'), t('shadow.source')]
            .map(label => <th key={label} scope="col">{label}</th>)}
        </tr></thead><tbody>{observations.map((o, index) => <tr key={index}>
          <td>{o.requirementId}<small>{o.assetId} · {o.quantity}</small></td>
          <td>{new Intl.NumberFormat(undefined, { maximumSignificantDigits: 17 }).format(o.value)} {o.unit}</td><td>{o.quality}</td>
          <td>{formatDetailTimestamp(o.timestampUtc)}</td><td>{o.source}</td>
        </tr>)}</tbody></table></div>
      </section>}
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
