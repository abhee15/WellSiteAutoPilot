import { PanelLoading } from '../components/LoadingIndicator';
import { useSplitPaneFocus } from '../components/useSplitPaneFocus';
import { useEffect, useMemo, useState } from 'react';
import { useHashDetail } from '../routing';
import type {
  AssetResponse, ConfiguredLogicResponse, ExecutionResponse,
} from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { EmptyRow, PanelHeader } from '../components/common';
import { Status, StatusIndicator } from '../components/Status';
import { formatTimestamp, shortId } from '../format';
import { outcomeLabel } from '../words';
import { describeRunContext, executionAssetLabel } from './runContext';
import { useAssetIdentityDirectory } from './useAssetIdentityDirectory';
import { ExecutionDetailPanel } from './ExecutionDetailPanel';
import { OperationsViewControls } from './OperationsViewControls';
import { loadConnections } from './connections';
import { DEFAULT_OPERATIONS_VIEW, filterOperations } from './operationsViews';
import { ActionButton } from '../components/ActionButton';
import type { OperationsViewCriteria } from './operationsViews';
import './splitLayout.css';
import { parseRunHistory } from './assetActivity';
import { recommendationIsAwaiting } from './recommendationInbox';
import { RunRecommendations, useRunRecommendations } from './RunRecommendations';
import { AssetRunHistory } from './AssetRunHistory';

export function OperationsPage(props: Parameters<typeof RecentOperationsPage>[0]) {
  const [detail] = useHashDetail('operations');
  const scope = parseRunHistory(detail);
  if (scope) return <AssetRunHistory key={`${scope.assetId}:${scope.logicId}`} {...scope}
    assets={props.assets ?? []} configuredLogic={props.configuredLogic ?? []} />;
  return <RecentOperationsPage {...props} />;
}

function RecentOperationsPage({
  executions,
  userId,
  isLoaded = true,
  assets = [],
  configuredLogic = [],
  canReadConnections = false,
  canReadAssets = true,
  canReadRecommendations = false,
}: {
  executions: ExecutionResponse[];
  userId: string | null | undefined;
  isLoaded?: boolean;
  assets?: AssetResponse[];
  configuredLogic?: ConfiguredLogicResponse[];
  /** Whether this person may list saved connections, so a run can name the connection it read from. */
  canReadConnections?: boolean;
  canReadAssets?: boolean;
  canReadRecommendations?: boolean;
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
  // The global snapshot is bounded: the detail endpoint remains the authorization authority
  // when a selected run ages out of that snapshot.
  const hasSelection = selectedExecutionId !== null;
  const splitFocus = useSplitPaneFocus(hasSelection ? selectedExecutionId : null);
  const runAssetIds = useMemo(() => [...new Set(executions.flatMap(run =>
    describeRunContext(run, configuredLogic, assets).assetIds.slice(0, 2)))].slice(0, 100),
  [executions, configuredLogic, assets]);
  const assetDirectory = useAssetIdentityDirectory(runAssetIds, assets, canReadAssets, userId);
  const visibleExecutions = useMemo(
    () => filterOperations(executions, viewCriteria, row => {
      const context = describeRunContext(row, configuredLogic, assetDirectory.assets, assetDirectory.owners);
      return [context.logicName, ...context.assetLabels];
    }),
    [executions, viewCriteria, configuredLogic, assetDirectory.assets, assetDirectory.owners],
  );
  const recommendations = useRunRecommendations(canReadRecommendations);
  const pendingRuns = new Set(recommendations.items.filter(item => recommendationIsAwaiting(item, Date.now()))
    .map(item => item.executionId));

  return (
    <>
      <div {...splitFocus} className="split-layout runs-layout operations-layout" data-has-selection={hasSelection}>
      <div className="split-panes">
      <section className="panel split-browse">
        <PanelHeader title="Recent executions" count={visibleExecutions.length} />
        <OperationsViewControls
          userId={userId}
          criteria={viewCriteria}
          onChange={setViewCriteria}
          matchingCount={visibleExecutions.length}
          recentCount={executions.length}
        />
        {!isLoaded ? <PanelLoading label={t('views.loading')} /> : <div className="table-wrap">
          <table className="activity-table">
            <thead>
              <tr>
                <th>Status</th>
                <th>Asset</th>
                <th className="run-col-logic">Logic module</th>
                <th className="run-col-secondary">Mode</th>
                <th className="run-col-secondary">Requested</th>
                <th className="run-col-secondary">Outcome</th>
              </tr>
            </thead>
            <tbody>
              {visibleExecutions.map(execution => {
                const context = describeRunContext(execution, configuredLogic, assetDirectory.assets, assetDirectory.owners);
                return (
                <tr key={execution.executionId} data-selected={hasSelection && selectedExecutionId === execution.executionId}
                  onClick={event => {
                    // The row is a convenience; the asset name button stays the keyboard and screen-reader route.
                    if ((event.target as HTMLElement).closest('button, a')) return;
                    if (execution.executionId) setSelectedExecutionId(execution.executionId);
                  }}>
                  <td>
                    <Status value={execution.status} />
                    {pendingRuns.has(execution.executionId) && <span className="run-pending-compact">
                      <StatusIndicator tone="warning" label={t('operations.awaitingDecision')} />
                    </span>}
                  </td>
                  <td>
                    <button
                      className="row-link"
                      type="button"
                      disabled={!execution.executionId}
                      aria-current={hasSelection && selectedExecutionId === execution.executionId ? 'true' : undefined}
                      aria-controls={hasSelection &&
                        selectedExecutionId === execution.executionId
                          ? 'execution-details'
                          : undefined}
                      aria-expanded={hasSelection &&
                        selectedExecutionId === execution.executionId}
                      aria-label={t('execution.inspectFor', {
                        id: shortId(execution.executionId),
                      })}
                      onClick={() => setSelectedExecutionId(current =>
                        current === execution.executionId ? null : execution.executionId ?? null)}
                    >
                      <strong>{executionAssetLabel(execution, context)}</strong>
                    </button>
                    {execution.quantity && <small>{execution.quantity}</small>}
                    <span className="run-compact-context">
                      <small>{context.logicName ?? execution.moduleId} · v{execution.moduleVersion}</small>
                      <small><OutcomeCell execution={execution} /></small>
                    </span>
                  </td>
                  <td className="run-col-logic">
                    <strong>{context.logicName ?? execution.moduleId}</strong>
                    <small>{context.logicName
                      ? `${execution.moduleId} v${execution.moduleVersion}${context.revisionNumber ? ' · ' + t('setup.revision', { number: context.revisionNumber }) : ''}`
                      : `v${execution.moduleVersion}`}</small>
                  </td>
                  <td className="run-col-secondary">{execution.mode}</td>
                  <td className="activity-time run-col-secondary">{formatTimestamp(execution.requestedAtUtc)}</td>
                  <td className="run-col-secondary">
                    {pendingRuns.has(execution.executionId) ?
                      <StatusIndicator tone="warning" label={t('operations.awaitingDecision')} /> :
                      <OutcomeCell execution={execution} />}
                  </td>
                </tr>
                );
              })}
              {visibleExecutions.length === 0 && (
                <EmptyRow columns={6} loading={!isLoaded} hint={isLoaded && executions.length === 0 ? t('views.noActivityHint') : undefined}
                  action={isLoaded && executions.length > 0 ? <ActionButton label={t('views.clearFilters')} onClick={() => setViewCriteria(DEFAULT_OPERATIONS_VIEW)} /> : undefined}>{!isLoaded ? t('views.loading') : executions.length === 0
                  ? t('views.noActivity')
                  : t('views.noMatches')}</EmptyRow>
              )}
            </tbody>
          </table>
        </div>}
      </section>
      <section className="split-selected" tabIndex={-1} aria-label={t('execution.heading')}>
        <ActionButton label={t('execution.backToList')} className="split-back" onClick={() => setSelectedExecutionId(null)} />
        {selectedExecutionId && (
          <ExecutionDetailPanel
            key={selectedExecutionId}
            executionId={selectedExecutionId}
            // A run opened by link may be older than the loaded list; the server decides if it may be seen.
            onAccessLost={() => setSelectedExecutionId(null)}
            configuredLogic={configuredLogic}
            assets={assetDirectory.assets}
            assetOwners={assetDirectory.owners}
            connectionNames={connectionNames}
            context={canReadRecommendations && <RunRecommendations key={selectedExecutionId}
              executionId={selectedExecutionId} assets={assetDirectory.assets} userId={userId}
              canReadAssets={canReadAssets} />}
            poll
            onClose={() => setSelectedExecutionId(null)}
          />
        )}
      </section>
      </div>
      </div>
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

