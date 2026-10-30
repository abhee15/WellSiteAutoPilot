import { useEffect, useRef, useState } from 'react';
import { RefreshCw } from 'lucide-react';
import { ActionButton } from '../components/ActionButton';
import type { AssetResponse, ConfiguredLogicResponse, ExecutionResponse } from '../api/generated/v1/types.gen';
import { hashFor } from '../routing';
import { modeSentence, outcomeLabel, triggerLabel } from '../words';
import { Status } from '../components/Status';
import { shortId } from '../format';
import { useChangeRefresh, useLiveUpdates } from '../live/LiveUpdates';
import { CopyButton } from './CopyButton';
import { describeRunContext } from './runContext';
import { t } from '../localization';
import {
  ExecutionDetailError,
  fetchExecutionDetail,
  formatDetailTimestamp,
  IN_FLIGHT_EXECUTION_STATUSES,
  previewExecutionOutput,
  executionInputObservations,
  executionProvenance,
  structuredExecutionValues,
  ageWhenRead,
  describeExecutionOutput,
  humanizeName,
  isGuid,
  prettyExecutionOutput,
  runDuration,
} from './executionDetail';
import type { ExecutionDetailErrorKind } from './executionDetail';
import './executionSummary.css';
import { LoadingIndicator } from '../components/LoadingIndicator';

type Props = {
  executionId: string;
  onClose: () => void;
  poll?: boolean;
  onObserved?: (detail: ExecutionResponse) => void;
  onAccessLost?: () => void;
  configuredLogic?: readonly ConfiguredLogicResponse[];
  assets?: readonly AssetResponse[];
  /** Saved connection names by id, so a run reads "North field gateway" and not a GUID. Absent when the person may not list connections. */
  connectionNames?: ReadonlyMap<string, string>;
};

/** While a run is in flight: refetch on a change signal, and poll as a fallback (often when no live connection exists). */
const FALLBACK_REFRESH_MS = 2000;
const LIVE_SAFETY_REFRESH_MS = 15000;

export function ExecutionDetailPanel({ executionId, onClose, poll, onObserved, onAccessLost,
  configuredLogic = [], assets = [], connectionNames }: Props) {
  const [detail, setDetail] = useState<ExecutionResponse | null>(null);
  const [error, setError] = useState<ExecutionDetailErrorKind | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [refreshNumber, setRefreshNumber] = useState(0);

  const callbacks = useRef({ onObserved, onAccessLost });
  useEffect(() => { callbacks.current = { onObserved, onAccessLost }; }, [onObserved, onAccessLost]);
  const { state: liveState } = useLiveUpdates();
  const inFlight = detail !== null && IN_FLIGHT_EXECUTION_STATUSES.includes(detail.status ?? '');
  const inFlightNow = useRef(false);
  useEffect(() => { inFlightNow.current = inFlight; }, [inFlight]);
  // The Server names the area that changed and nothing else, so a run that is still going is read again.
  useChangeRefresh(['executions'], () => { if (inFlightNow.current) setRefreshNumber(n => n + 1); });
  useEffect(() => {
    if (!poll || !inFlight) return;
    const timer = window.setTimeout(() => setRefreshNumber(n => n + 1),
      liveState === 'connected' ? LIVE_SAFETY_REFRESH_MS : FALLBACK_REFRESH_MS);
    return () => window.clearTimeout(timer);
  }, [detail, poll, inFlight, liveState]);

  useEffect(() => {
    const controller = new AbortController();
    let active = true;
    // The panel remounts for each run, so the first load starts empty; a refresh keeps what is on screen
    // until the new answer arrives, and keeps it if the refresh fails.
    setIsLoading(true);

    void fetchExecutionDetail(executionId, controller.signal)
      .then(value => {
        if (active && !controller.signal.aborted) {
          setDetail(value);
          setError(null);
          setIsLoading(false);
          callbacks.current.onObserved?.(value);
        }
      })
      .catch(reason => {
        if (!active || controller.signal.aborted) return;
        if (reason instanceof ExecutionDetailError && ['accessDenied', 'notFound'].includes(reason.kind)) {
          callbacks.current.onAccessLost?.();
          setDetail(null);
        }
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
        <h3>{t('execution.heading')}</h3>
        <div className="execution-detail-actions">
          <ActionButton icon={RefreshCw} iconOnly label={t('execution.refresh')} busy={isLoading}
            onClick={() => setRefreshNumber(value => value + 1)} />
          <ActionButton label={t('execution.close')} onClick={onClose} />
        </div>
      </div>
      {isLoading && !detail && <LoadingIndicator label={t('execution.loading')} />}
      {error && <p role="alert" className="execution-detail-error">{errorMessages[error]}</p>}
      {detail && <ExecutionDetailContent detail={detail} configuredLogic={configuredLogic} assets={assets}
        connectionNames={connectionNames} />}
    </section>
  );
}

/** The saved connection's name, or a short id when the name is not known to this person. */
function sourceName(connectionId: string | null | undefined, names?: ReadonlyMap<string, string>): string {
  if (!connectionId || connectionId === '—') return '—';
  // Older runs recorded a plain source label rather than a connection id; show that as it is.
  if (!isGuid(connectionId)) return connectionId;
  return names?.get(connectionId) ?? t('execution.connectionUnnamed', { id: connectionId });
}

/** Pure view to keep response rendering testable independently of network effects. */
export function ExecutionDetailContent({ detail, configuredLogic = [], assets = [], connectionNames }: {
  detail: ExecutionResponse;
  configuredLogic?: readonly ConfiguredLogicResponse[];
  assets?: readonly AssetResponse[];
  connectionNames?: ReadonlyMap<string, string>;
}) {
  const context = describeRunContext(detail, configuredLogic, assets);
  const outcome = outcomeLabel(detail.resultCode ?? detail.failureCode);
  const provenance = executionProvenance(detail);
  const duration = runDuration(detail);
  const observations = executionInputObservations(detail.outputJson);
  const resultRows = describeExecutionOutput(detail.outputJson);
  const structuredValues = structuredExecutionValues(detail.outputJson);
  const raw = previewExecutionOutput(prettyExecutionOutput(detail.outputJson));
  const technical: Array<[string, string | null | undefined, boolean]> = [
    [t('execution.logicInstance'), detail.logicInstanceId, true],
    [t('execution.revision'), detail.configurationRevisionId, true],
    [t('execution.asset'), detail.assetExternalId ?? detail.assetId, false],
    [t('execution.quantity'), detail.quantity, false],
    [t('execution.contract'), String(detail.requestContractVersion ?? '—'), false],
    [t('execution.pinnedPackage'), provenance?.packageSha256, true],
    [t('execution.result'), detail.resultCode, false],
    [t('execution.failure'), detail.failureCode, false],
    [t('execution.correlation'), detail.correlationId, true],
    [t('execution.id'), detail.executionId, true],
  ];

  return (
    <>
      <div className="run-summary" aria-label={t('execution.summary')}>
        <p className="run-summary-lead">
          <Status value={detail.status} />
          {outcome.text && <span title={outcome.code ?? undefined}>{outcome.text}</span>}
        </p>
        <dl className="run-facts">
          <div><dt>{t('execution.summaryLogic')}</dt><dd>
            {detail.logicInstanceId && context.logicName
              ? <a href={hashFor('logic', detail.logicInstanceId)}>{context.logicName}</a>
              : (detail.moduleId ?? '—')}
            {context.revisionNumber ? ` · ${t('setup.revision', { number: context.revisionNumber })}` : ''}
          </dd></div>
          {context.assetIds.length > 0 && <div><dt>{t('execution.summaryAssets')}</dt><dd>
            {context.assetIds.map((id, index) => <span key={id}>
              {index > 0 && ', '}<a href={hashFor('assets', id)}>{context.assetLabels[index]}</a>
            </span>)}
          </dd></div>}
          <div><dt>{t('execution.runType')}</dt><dd>
            {triggerLabel(detail.trigger)}
            {detail.scheduledForUtc && <small>{t('execution.scheduledFor', { time: formatDetailTimestamp(detail.scheduledForUtc) })}</small>}
          </dd></div>
          {detail.mode && <div><dt>{t('execution.mode')}</dt><dd>{modeSentence(detail.mode)}</dd></div>}
          <div><dt>{t('execution.module')}</dt><dd>
            {[detail.moduleId, detail.moduleVersion].filter(Boolean).join(' · ') || '—'}</dd></div>
          <div><dt>{t('execution.requested')}</dt><dd>{formatDetailTimestamp(detail.requestedAtUtc)}</dd></div>
          {detail.completedAtUtc && <div><dt>{t('execution.completed')}</dt><dd>
            {formatDetailTimestamp(detail.completedAtUtc)}
            {duration && <small>{t('execution.duration')}: {duration}</small>}
          </dd></div>}
        </dl>
      </div>
      {structuredValues.length > 0 && <section className="run-section" aria-label={t('execution.structured')}>
        <h4>{t('execution.structured')}</h4>
        {structuredValues.map((value, index) => <details key={index}>
          <summary>{t(value.kind === 'Input' ? 'execution.structuredInput' : 'execution.structuredResult')}: {humanizeName(value.label)} · {value.schemaId}</summary>
          <pre>{value.json}</pre>
        </details>)}
      </section>}
      {observations.length > 0 && <section className="run-section" aria-label={t('shadow.observations')}>
        <h4>{t('shadow.observations')}</h4>
        <ul className="run-inputs">{observations.map((o, index) => {
          const input = outcomeLabel(o.outcomeCode);
          const age = ageWhenRead(o.timestampUtc, o.retrievedAtUtc);
          return <li key={index} className="run-input">
            <div className="run-input-head">
              <strong>{humanizeName(o.requirementId)}</strong>
              <span>{assets.find(asset => asset.id === o.assetId)?.name ?? shortId(o.assetId)}</span>
            </div>
            <p className="run-input-value">
              {o.value === null ? '—' : new Intl.NumberFormat(undefined, { maximumSignificantDigits: 17 }).format(o.value)}
              {o.unit ? ` ${o.unit}` : ''}
              <Status value={o.quality} />
            </p>
            <dl className="run-input-facts">
              <div><dt>{t('execution.measuredAt')}</dt><dd>{formatDetailTimestamp(o.timestampUtc)}
                {age && <small>{age}</small>}
                {o.timestampKind === 'UnverifiedScan' && <small>{t('execution.unverifiedScan')}</small>}</dd></div>
              <div><dt>{t('execution.retrieved')}</dt><dd>{formatDetailTimestamp(o.retrievedAtUtc)}</dd></div>
              <div><dt>{t('shadow.source')}</dt><dd>{o.source === '—' ? '—' : sourceName(o.source, connectionNames)}</dd></div>
              {input.text && <div><dt>{t('execution.inputOutcome')}</dt><dd title={input.code ?? undefined}>{input.text}</dd></div>}
            </dl>
          </li>;
        })}</ul>
      </section>}
      {provenance && provenance.connections!.length > 0 && <section className="run-section" aria-label={t('execution.savedSources')}>
        <h4>{t('execution.savedSources')}</h4>
        <ul>{provenance.connections!.map(connection => <li key={`${connection.connectionId}:${connection.revision}`}>
          {t('execution.connectionRevision', { connection: sourceName(connection.connectionId, connectionNames),
            revision: connection.revision ?? 0, count: connection.assetCount ?? 0 })}
        </li>)}</ul>
        {provenance.connectionsTruncated && <p>{t('execution.connectionsTruncated')}</p>}
      </section>}
      {resultRows.length > 0 && <section className="run-section" aria-label={t('execution.result.heading')}>
        <h4>{t('execution.result.heading')}</h4>
        <dl className="run-facts">{resultRows.map(row => <div key={row.label}><dt>{row.label}</dt><dd>{row.value}</dd></div>)}</dl>
      </section>}
      <details className="run-technical">
        <summary>{t('execution.technical')}</summary>
        <dl className="execution-detail-grid">
          {technical.map(([label, value, copyable]) => <div key={label}>
            <dt>{label}</dt>
            <dd>{value || '—'}{copyable && value && <CopyButton value={value} label={label} />}</dd>
          </div>)}
        </dl>
        {raw.text && <>
          <h4>{t('execution.rawOutput')}</h4>
          <pre className="execution-detail-output">{raw.text}</pre>
          {raw.truncated && <p>{t('execution.truncated')}</p>}
        </>}
      </details>
    </>
  );
}
