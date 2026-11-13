import { useEffect, useMemo, useState } from 'react';
import { ChevronDown, ChevronRight, ChevronsDownUp, ChevronsUpDown } from 'lucide-react';
import { ActionButton } from '../components/ActionButton';
import { DataTable } from '../components/DataTable';
import { PanelLoading } from '../components/LoadingIndicator';
import { EmptyRow } from '../components/common';
import { Status } from '../components/Status';
import { formatTimestamp } from '../format';
import { t } from '../localization';
import { statusLabel } from '../words';
import type { AssetResponse, ExecutionResponse } from '../api/generated/v1/types.gen';
import type { OwnerGroup } from './assetHierarchy';
import { loadViews } from './assetViewsApi';
import { loadConnections } from './connections';
import { groupConfigurations, listedStatus } from './configuredLogicViews';
import { configurationScheduleText } from './logicSummary';
import { configuredTargetLabel, latestRunByConfiguration, shortId } from './runContext';
import { useLiveResource } from './useLiveResource';

type Props = {
  groups: ReturnType<typeof groupConfigurations>;
  selectedId: string | null;
  assets: readonly AssetResponse[];
  owners: readonly OwnerGroup[];
  executions?: readonly ExecutionResponse[];
  canReadConnections: boolean;
  userId: string | null | undefined;
  isLoaded: boolean;
  totalCount: number;
  onClearFilters: () => void;
  onSelect: (id: string | null) => void;
};

export function ConfigurationList({
  groups,
  selectedId,
  assets,
  owners,
  executions,
  canReadConnections,
  userId,
  isLoaded,
  totalCount,
  onClearFilters,
  onSelect,
}: Props) {
  const [collapsed, setCollapsed] = useState<ReadonlySet<string>>(() => new Set());
  const selectedGroup = groups.find((group) => group.deployments.some(({ item }) => item.id === selectedId))?.key;
  useEffect(() => {
    if (!selectedGroup) return;
    setCollapsed((current) => {
      if (!current.has(selectedGroup)) return current;
      const next = new Set(current);
      next.delete(selectedGroup);
      return next;
    });
  }, [selectedGroup, selectedId]);
  const identity = userId ?? '';
  const views = useLiveResource(identity, loadViews, ['assets', 'connections'], canReadConnections && totalCount > 0);
  const connections = useLiveResource(identity, loadConnections, ['connections'], canReadConnections && totalCount > 0);
  // Do not reuse cached identities when a read fails or access to a source is lost.
  const viewsById = useMemo(
    () => new Map((views.failed ? [] : (views.data ?? [])).map((view) => [view.id, view])),
    [views.data, views.failed],
  );
  const connectionsById = useMemo(
    () => new Map((connections.failed ? [] : (connections.data ?? [])).map((item) => [item.id, item])),
    [connections.data, connections.failed],
  );
  const assetsById = useMemo(() => new Map(assets.map((asset) => [asset.id, asset])), [assets]);
  const ownersById = useMemo(() => new Map(owners.map((owner) => [owner.connectionId, owner.name])), [owners]);
  const lastRuns = useMemo(() => latestRunByConfiguration(executions ?? []), [executions]);
  const showRuns = executions !== undefined;

  if (!isLoaded) return <PanelLoading label={t('views.loading')} />;
  const allCollapsed = groups.length > 0 && groups.every((group) => collapsed.has(group.key));
  return (
    <>
      {groups.length > 1 && (
        <div className="configuration-list-toolbar">
          <ActionButton
            variant="quiet"
            icon={allCollapsed ? ChevronsUpDown : ChevronsDownUp}
            label={t(allCollapsed ? 'configured.expandAll' : 'configured.collapseAll')}
            onClick={() => setCollapsed(allCollapsed ? new Set() : new Set(groups.map((group) => group.key)))}
          />
        </div>
      )}
      <DataTable className="configuration-list" label={t('configured.tableLabel')}>
        <thead>
          <tr>
            <th>{t('configured.colStatus')}</th>
            <th>{t('configured.colLogic')}</th>
            <th className="run-col-secondary">{t('configured.colRevision')}</th>
            {showRuns && <th className="run-col-secondary">{t('configured.colLastRun')}</th>}
          </tr>
        </thead>
        {groups.map((group) => {
          const expanded = !collapsed.has(group.key);
          const holdsSelection = group.key === selectedGroup;
          return (
            <tbody key={group.key}>
              <tr className="configuration-group" data-holds-selection={holdsSelection}>
                <td colSpan={showRuns ? 4 : 3}>
                  <ActionButton
                    variant="quiet"
                    icon={expanded ? ChevronDown : ChevronRight}
                    label={group.name || t('configured.moduleUnavailable')}
                    aria-expanded={expanded}
                    onClick={() =>
                      setCollapsed((current) => {
                        const next = new Set(current);
                        if (expanded) next.add(group.key);
                        else next.delete(group.key);
                        return next;
                      })
                    }
                  />
                  {group.moduleId && group.moduleId !== group.name && <small>{group.moduleId}</small>}
                  <small>
                    {[
                      t(group.deployments.length === 1 ? 'configured.oneDeployment' : 'configured.deployments', {
                        count: group.deployments.length,
                      }),
                      t(group.assets.size === 1 ? 'configured.oneAsset' : 'configured.assets', {
                        count: group.assets.size,
                      }),
                      t('configured.liveCount', { count: group.active }),
                      group.latestApproved && t('configured.latestApproved', { version: group.latestApproved }),
                    ]
                      .filter(Boolean)
                      .join(' · ')}
                  </small>
                  {holdsSelection && !expanded && (
                    <span className="configuration-group-chip">{t('configured.selectedChip')}</span>
                  )}
                </td>
              </tr>
              {expanded &&
                group.deployments.map(({ item, revision, assetIds }) => {
                  const selected = selectedId !== null && selectedId === item.id;
                  const active = Boolean(item.activeRevisionId && revision);
                  const viewRef = revision?.view;
                  const view = viewRef?.viewId ? viewsById.get(viewRef.viewId) : undefined;
                  const connectionIds = new Set<string>();
                  if (viewRef?.connectionId) connectionIds.add(viewRef.connectionId);
                  for (const id of assetIds) {
                    const owner = assetsById.get(id)?.ownerConnectionId;
                    if (owner) connectionIds.add(owner);
                  }
                  const providers = [
                    ...new Set(
                      revision?.dataBindings?.flatMap((binding) => (binding.providerId ? [binding.providerId] : [])) ??
                        [],
                    ),
                  ];
                  const connectionLabels = [...connectionIds].map((id) => {
                    const connection = connectionsById.get(id);
                    const name = connection?.name ?? ownersById.get(id) ?? shortId(id);
                    return connection?.adapterKey ? `${name} (${connection.adapterKey})` : name;
                  });
                  const schedule = configurationScheduleText(item, revision);
                  const run = lastRuns.get(item.id ?? '');
                  const drafts = item.revisions?.filter((revision) => revision.status === 'Draft').length ?? 0;
                  const version = revision?.moduleVersion
                    ? t(active ? 'configured.activeVersion' : 'configured.revisionVersion', {
                        version: revision.moduleVersion,
                      })
                    : t('configured.versionUnavailable');
                  return (
                    <tr
                      key={item.id}
                      data-selected={selected}
                      onClick={(event) => {
                        if (item.id && !(event.target as HTMLElement).closest('button, a')) onSelect(item.id);
                      }}
                    >
                      <td>
                        <Status
                          value={
                            item.activeRevisionId && !revision ? t('configured.versionUnavailable') : listedStatus(item)
                          }
                        />
                      </td>
                      <td>
                        <button
                          type="button"
                          className="asset-select"
                          disabled={!item.id}
                          aria-expanded={selected}
                          aria-current={selected ? 'true' : undefined}
                          aria-controls={selected ? 'configured-revisions' : undefined}
                          data-split-focus-key={item.id ?? undefined}
                          aria-label={t('configured.inspectFor', { name: item.name ?? '—' })}
                          onClick={() => {
                            if (item.id) onSelect(selected ? null : item.id);
                          }}
                        >
                          {item.name}
                        </button>
                        {connectionLabels.length > 0 && <small>{connectionLabels.join(', ')}</small>}
                        {providers.length > 0 && (
                          <small>{t('configured.providers', { names: providers.join(', ') })}</small>
                        )}
                        {viewRef && (
                          <small>
                            {t('configured.assetView', {
                              view:
                                view?.connectionId === viewRef.connectionId && view
                                  ? view.name
                                  : shortId(viewRef.viewId ?? '—'),
                              revision: viewRef.revision ?? '—',
                            })}
                          </small>
                        )}
                        <small>{configuredTargetLabel(assetIds, assets, owners)}</small>
                        <small>
                          {t(assetIds.length === 1 ? 'configured.oneAsset' : 'configured.assets', {
                            count: assetIds.length,
                          })}
                        </small>
                        <small>
                          {[version, revision?.mode ? statusLabel(revision.mode) : null, schedule]
                            .filter(Boolean)
                            .join(' · ')}
                        </small>
                        {drafts > 0 && <small>{t('logic.rowDrafts', { count: drafts })}</small>}
                      </td>
                      <td className="run-col-secondary">
                        {revision?.revisionNumber ? t('setup.revision', { number: revision.revisionNumber }) : '—'}
                      </td>
                      {showRuns && (
                        <td className="run-col-secondary">
                          {run
                            ? `${formatTimestamp(run.requestedAtUtc)} · ${statusLabel(run.status)}`
                            : t('configured.noRecentRun')}
                        </td>
                      )}
                    </tr>
                  );
                })}
            </tbody>
          );
        })}
        {groups.length === 0 && (
          <tbody>
            <EmptyRow
              columns={showRuns ? 4 : 3}
              hint={totalCount === 0 ? t('configured.noneHint') : undefined}
              action={
                totalCount > 0 ? <ActionButton label={t('views.clearFilters')} onClick={onClearFilters} /> : undefined
              }
            >
              {t(totalCount === 0 ? 'configured.none' : 'configured.noMatches')}
            </EmptyRow>
          </tbody>
        )}
      </DataTable>
    </>
  );
}
