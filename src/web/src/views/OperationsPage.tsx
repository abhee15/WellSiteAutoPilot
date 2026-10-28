import { useEffect, useMemo, useState } from 'react';
import { useForgetVanishedSelection, useHashDetail } from '../routing';
import type {
  AssetResponse, ConfiguredLogicResponse, ExecutionResponse,
} from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { EmptyRow, PanelHeader, StatusPill, SummaryCard } from '../components/common';
import { formatTimestamp, shortId } from '../format';
import { outcomeLabel } from '../words';
import { describeRunContext, summarizeAssetLabels } from './runContext';
import { ExecutionDetailPanel } from './ExecutionDetailPanel';
import { OperationsViewControls } from './OperationsViewControls';
import { loadConnections } from './connections';
import { DEFAULT_OPERATIONS_VIEW, filterOperations } from './operationsViews';
import type { OperationsViewCriteria } from './operationsViews';

export function OperationsPage({
  executions,
  userId,
  isLoaded = true,
  assets = [],
  configuredLogic = [],
  canReadConnections = false,
}: {
  executions: ExecutionResponse[];
  userId: string | null | undefined;
  isLoaded?: boolean;
  assets?: AssetResponse[];
  configuredLogic?: ConfiguredLogicResponse[];
  /** Whether this person may list saved connections, so a run can name the connection it read from. */
  canReadConnections?: boolean;
}) {
  const [connectionNames, setConnectionNames] = useState<ReadonlyMap<string, string>>(new Map());
  useEffect(() => {
    if (!canReadConnections) return;
    const controller = new AbortController();
    loadConnections(controller.signal)
      .then(list => { if (!controller.signal.aborted) setConnectionNames(new Map(list.map(item => [item.id, item.name]))); })
      .catch(() => { /* names are a convenience; a run still shows a short connection id */ });
    return () => controller.abort();
  }, [canReadConnections]);
  const [viewCriteria, setViewCriteria] = useState<OperationsViewCriteria>(DEFAULT_OPERATIONS_VIEW);
  const [selectedExecutionId, setSelectedExecutionId] = useHashDetail('operations');
  const selectedIdStillAuthorized = selectedExecutionId !== null &&
    executions.some(execution => execution.executionId === selectedExecutionId);
  useForgetVanishedSelection(selectedExecutionId, selectedIdStillAuthorized, () => setSelectedExecutionId(null));
  const visibleExecutions = useMemo(
    () => filterOperations(executions, viewCriteria, row => {
      const context = describeRunContext(row, configuredLogic, assets);
      return [context.logicName, ...context.assetLabels];
    }),
    [executions, viewCriteria, configuredLogic, assets],
  );
  const activeExecutions = executions.filter(
    execution =>
      execution.status === 'Requested' ||
      execution.status === 'Queued' ||
      execution.status === 'Starting' ||
      execution.status === 'Running' ||
      execution.status === 'Waiting',
  ).length;
  const attentionExecutions = executions.filter(
    execution =>
      execution.status === 'Failed' ||
      execution.status === 'Suspended' ||
      execution.status === 'TimedOut',
  ).length;

  return (
    <>
      <section className="summary-grid" aria-label="Operations summary">
        <SummaryCard
          label="Active executions"
          value={String(activeExecutions)}
          detail="Requested through waiting"
        />
        <SummaryCard
          label="Requires attention"
          value={String(attentionExecutions)}
          detail="Failed, suspended, or timed out"
        />
      </section>

      <section className="panel">
        <PanelHeader eyebrow="Activity" title="Recent executions" count={visibleExecutions.length} />
        <OperationsViewControls
          userId={userId}
          criteria={viewCriteria}
          onChange={setViewCriteria}
          matchingCount={visibleExecutions.length}
          recentCount={executions.length}
        />
        <div className="table-wrap">
          <table className="activity-table">
            <thead>
              <tr>
                <th>Status</th>
                <th>Asset</th>
                <th>Logic Module</th>
                <th>Mode</th>
                <th>Requested</th>
                <th>Outcome</th>
                <th>{t('execution.detailsColumn')}</th>
              </tr>
            </thead>
            <tbody>
              {visibleExecutions.map(execution => {
                const context = describeRunContext(execution, configuredLogic, assets);
                return (
                <tr key={execution.executionId}>
                  <td>
                    <StatusPill value={execution.status} />
                  </td>
                  <td>
                    <strong>{assetText(execution, context)}</strong>
                    {execution.quantity && <small>{execution.quantity}</small>}
                  </td>
                  <td>
                    <strong>{context.logicName ?? execution.moduleId}</strong>
                    <small>{context.logicName
                      ? `${execution.moduleId} v${execution.moduleVersion}${context.revisionNumber ? ' · ' + t('setup.revision', { number: context.revisionNumber }) : ''}`
                      : `v${execution.moduleVersion}`}</small>
                  </td>
                  <td>{execution.mode}</td>
                  <td className="activity-time">{formatTimestamp(execution.requestedAtUtc)}</td>
                  <td>
                    <OutcomeCell execution={execution} />
                  </td>
                  <td>
                    <button
                      className="execution-inspect-button"
                      type="button"
                      disabled={!execution.executionId}
                      aria-controls={selectedIdStillAuthorized &&
                        selectedExecutionId === execution.executionId
                          ? 'execution-details'
                          : undefined}
                      aria-expanded={selectedIdStillAuthorized &&
                        selectedExecutionId === execution.executionId}
                      aria-label={t('execution.inspectFor', {
                        id: shortId(execution.executionId),
                      })}
                      onClick={() => setSelectedExecutionId(current =>
                        current === execution.executionId ? null : execution.executionId ?? null)}
                    >
                      {t('execution.inspect')}
                    </button>
                  </td>
                </tr>
                );
              })}
              {visibleExecutions.length === 0 && (
                <EmptyRow columns={7} loading={!isLoaded} hint={isLoaded && executions.length === 0 ? t('views.noActivityHint') : undefined}>{!isLoaded ? t('views.loading') : executions.length === 0
                  ? t('views.noActivity')
                  : t('views.noMatches')}</EmptyRow>
              )}
            </tbody>
          </table>
        </div>
        {selectedExecutionId && (
          <ExecutionDetailPanel
            key={selectedExecutionId}
            executionId={selectedExecutionId}
            // A run opened by link may be older than the loaded list; the server decides if it may be seen.
            onAccessLost={() => setSelectedExecutionId(null)}
            configuredLogic={configuredLogic}
            assets={assets}
            connectionNames={connectionNames}
            poll
            onClose={() => setSelectedExecutionId(null)}
          />
        )}
      </section>
    </>
  );
}

function OutcomeCell({ execution }: { execution: ExecutionResponse }) {
  const code = execution.failureCode ?? execution.resultCode;
  if (!code) {
    return <>{execution.status === 'Requested' ? 'Queued for runtime' : 'In progress'}</>;
  }
  const outcome = outcomeLabel(code);
  return <span title={outcome.code ?? undefined}>{outcome.text}</span>;
}

function assetText(execution: ExecutionResponse, context: { assetLabels: string[] }): string {
  if (execution.assetExternalId) return execution.assetExternalId;
  if (context.assetLabels.length > 0) return summarizeAssetLabels(context.assetLabels);
  return execution.assetId ? shortId(execution.assetId) : '—';
}
