import { useMemo, useState } from 'react';
import { useForgetVanishedSelection, useHashDetail } from '../routing';
import type {
  AssetResponse, ConfiguredLogicResponse, ExecutionResponse, SystemInfoResponse,
} from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { EmptyRow, PanelHeader, StatusPill, SummaryCard } from '../components/common';
import { formatTimestamp, shortId, toCssToken } from '../format';
import { outcomeLabel } from '../words';
import { describeRunContext, summarizeAssetLabels } from './runContext';
import { ExecutionDetailPanel } from './ExecutionDetailPanel';
import { OperationsViewControls } from './OperationsViewControls';
import { DEFAULT_OPERATIONS_VIEW, filterOperations } from './operationsViews';
import type { OperationsViewCriteria } from './operationsViews';

export function OperationsPage({
  systemInfo,
  executions,
  userId,
  isLoaded = true,
  assets = [],
  configuredLogic = [],
}: {
  systemInfo: SystemInfoResponse | null;
  executions: ExecutionResponse[];
  userId: string | null | undefined;
  isLoaded?: boolean;
  assets?: AssetResponse[];
  configuredLogic?: ConfiguredLogicResponse[];
}) {
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
  const components = systemInfo?.components ?? [];
  const unhealthyComponents = components.filter(
    component => component.status !== 'Healthy',
  ).length;
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
          label="Runtime health"
          value={
            components.length === 0
              ? 'Unknown'
              : unhealthyComponents === 0
                ? 'Healthy'
                : 'Attention'
          }
          detail={
            components.length === 0
              ? 'Waiting for runtime status'
              : `${components.length - unhealthyComponents}/${components.length} components healthy`
          }
        />
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
                  <td>{formatTimestamp(execution.requestedAtUtc)}</td>
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
                <EmptyRow columns={7} hint={isLoaded && executions.length === 0 ? t('views.noActivityHint') : undefined}>{!isLoaded ? t('views.loading') : executions.length === 0
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
            onClose={() => setSelectedExecutionId(null)}
          />
        )}
      </section>

      <section className="panel compact-panel">
        <PanelHeader eyebrow="Runtime node" title="Service health" />
        <div className="health-list">
          {components.map(component => (
            <div className="health-row" key={component.name}>
              <div>
                <strong>{component.name}</strong>
                <small>Version {component.version}</small>
              </div>
              <span className={`health-state health-${toCssToken(component.status)}`}>
                {component.status ?? 'Unknown'}
              </span>
            </div>
          ))}
          {!systemInfo && (
            <div className="status-loading">Loading runtime health…</div>
          )}
        </div>
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
