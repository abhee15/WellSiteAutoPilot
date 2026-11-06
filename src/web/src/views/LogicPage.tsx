import { useSplitPaneFocus } from '../components/useSplitPaneFocus';
import { useEffect, useMemo, useState } from 'react';
import { ActionButton } from '../components/ActionButton';
import { PageTabNav } from '../components/PageTabNav';
import { hashFor, useForgetVanishedSelection, useHashDetail } from '../routing';
import type {
  AssetResponse,
  AssetTypeResponse,
  ConfiguredLogicResponse,
  ExecutionResponse,
  LogicModuleResponse,
} from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { EmptyRow, PanelHeader } from '../components/common';
import { Status } from '../components/Status';
import { formatTimestamp } from '../format';
import { profileLabel, statusLabel } from '../words';
import { moduleStateLabel, moduleStateToken } from './moduleApproval';
import { configuredTargetLabel, latestRunByConfiguration } from './runContext';
import { useAssetIdentityDirectory } from './useAssetIdentityDirectory';
import { ConfiguredLogicControls } from './ConfiguredLogicControls';
import { ConfiguredLogicWorkspace } from './ConfiguredLogicWorkspace';
import { CONTROL_MANAGE_PERMISSION } from './controlExecution';
import { ModulePanel } from './ModulePanel';
import { SchedulesOverview } from './SchedulesOverview';
import { ModulePackageUpload } from './ModulePackageUpload';
import { LogicCatalogControls } from './LogicCatalogControls';
import { DEFAULT_CONFIGURED_LOGIC_CRITERIA, filterConfiguredLogic, listedStatus, orderedRevisions } from './configuredLogicViews';
import type { ConfiguredLogicCriteria } from './configuredLogicViews';
import { DEFAULT_LOGIC_CATALOG_CRITERIA, filterLogicCatalog, availableLogicRuntimes, groupModuleVersions } from './logicCatalog';
import type { LogicCatalogCriteria } from './logicCatalog';
import './splitLayout.css';

/** `#logic/modules` opens the Modules tab and `#logic/schedules` the Schedules tab. */
const MODULES_TAB = 'modules';
const SCHEDULES_TAB = 'schedules';

export function LogicPage({
  modules,
  configuredLogic,
  userId, assets, assetTypes, onRefresh, permissions, isLoaded = true, executions = [],
}: {
  modules: LogicModuleResponse[];
  configuredLogic: ConfiguredLogicResponse[];
  userId: string | null | undefined;
  assets: AssetResponse[];
  assetTypes: AssetTypeResponse[];
  onRefresh: () => Promise<void>;
  permissions: readonly string[];
  executions?: readonly ExecutionResponse[];
  isLoaded?: boolean;
}) {
  const canReadCatalog = permissions.includes('logic.read');
  const canReadConfigurations = permissions.includes('configured-logic.read');
  const canReadRuns = permissions.includes('executions.read');
  const lastRuns = useMemo(() => latestRunByConfiguration(executions), [executions]);
  const mayManage = permissions.includes('configured-logic.manage');
  const [catalogCriteria, setCatalogCriteria] = useState<LogicCatalogCriteria>(
    DEFAULT_LOGIC_CATALOG_CRITERIA,
  );
  const [configuredCriteria, setConfiguredCriteria] = useState<ConfiguredLogicCriteria>(
    DEFAULT_CONFIGURED_LOGIC_CRITERIA,
  );
  const [rawDetail, setSelectedConfiguredId] = useHashDetail('logic');
  // `#logic/new:<assetId>` starts setup from an asset (the "Put logic on this asset" link);
  // `#logic/modules` opens the Modules tab. Any other detail is a configuration id.
  const startViewId = rawDetail?.startsWith('new-view:') ? rawDetail.slice(9) : null;
  const startAssetId = rawDetail?.startsWith('new:') ? rawDetail.slice(4) : null;
  const modulesRequested = rawDetail === MODULES_TAB;
  const schedulesRequested = rawDetail === SCHEDULES_TAB;
  const selectedConfiguredId = startAssetId || startViewId || modulesRequested || schedulesRequested ? null : rawDetail;
  const showSchedules = canReadConfigurations && schedulesRequested;
  const showModules = !showSchedules && canReadCatalog && (modulesRequested || !canReadConfigurations);
  const [creating, setCreating] = useState(Boolean(startAssetId || startViewId));
  useEffect(() => { if (startAssetId || startViewId) setCreating(true); }, [startAssetId, startViewId]);
  const prefillAssetId = startAssetId && assets.some(asset => asset.id === startAssetId && asset.isActive)
    ? startAssetId : null;
  const [aboutModuleId, setAboutModuleId] = useState<string | null>(null);
  const aboutModule = modules.find(module => module.id === aboutModuleId);
  const canManage = mayManage && canReadConfigurations;
  const canCreate = canManage && (Boolean(startViewId) || assets.some(asset => asset.isActive));
  useEffect(() => { if (!canCreate) setCreating(false); }, [canCreate]);
  const selectedConfiguration = configuredLogic.find(item => item.id === selectedConfiguredId);
  useForgetVanishedSelection(selectedConfiguredId, Boolean(selectedConfiguration) || configuredLogic.length >= 100, () => setSelectedConfiguredId(null));
  const visibleConfigurations = useMemo(
    () => filterConfiguredLogic(configuredLogic, configuredCriteria),
    [configuredLogic, configuredCriteria],
  );
  // The grid shows two names per row. Resolve more for an open revision, without issuing
  // unbounded per-asset requests for a selection containing thousands of wells.
  const visibleTargetIds = useMemo(() => {
    if (showModules) return [];
    const selectedTargets = (selectedConfiguration?.revisions ?? []).flatMap(revision =>
      (revision.assetBindings ?? []).flatMap(binding => binding.assetId ? [binding.assetId] : [])).slice(0, 40);
    const gridTargets = visibleConfigurations.flatMap(item => (contextRevision(item)?.assetBindings ?? []).flatMap(binding => binding.assetId ? [binding.assetId] : []).slice(0, 2));
    return [...new Set([...selectedTargets, ...gridTargets])].slice(0, 100);
  }, [showModules, selectedConfiguration, visibleConfigurations]);
  const assetDirectory = useAssetIdentityDirectory(visibleTargetIds, assets, permissions.includes('assets.read'), userId);
  const visibleModules = useMemo(
    () => filterLogicCatalog(modules, catalogCriteria),
    [modules, catalogCriteria],
  );
  const moduleGroups = useMemo(() => groupModuleVersions(modules), [modules]);
  const visibleGroups = useMemo(() => groupModuleVersions(visibleModules), [visibleModules]);
  const selectedModuleVersions = moduleGroups.find(group => group.versions.some(version => version.id === aboutModuleId))?.versions ?? [];
  const runtimes = useMemo(() => availableLogicRuntimes(modules), [modules]);

  const splitFocus = useSplitPaneFocus(showModules ? aboutModule?.id ?? null
    : selectedConfiguredId ?? (creating && canCreate ? 'new' : null));
  const hasConfigurationDetail = selectedConfiguredId !== null || (creating && canCreate);

  return (
    <div {...splitFocus} className="split-layout runs-layout logic-layout" data-has-selection={showSchedules ? false : showModules ? Boolean(aboutModule) : hasConfigurationDetail}>
      <PageTabNav label={t('logic.tabs')}
        tabs={[
          ...(canReadConfigurations ? [
            { id: 'configured', label: t('logic.tabConfigured'), href: hashFor('logic'), count: configuredLogic.length },
            { id: SCHEDULES_TAB, label: t('logic.tabSchedules'), href: hashFor('logic', SCHEDULES_TAB) },
          ] : []),
          ...(canReadCatalog ? [
            { id: MODULES_TAB, label: t('logic.tabModules'), href: hashFor('logic', MODULES_TAB), count: moduleGroups.length },
          ] : []),
        ]}
        activeId={showSchedules ? SCHEDULES_TAB : showModules ? MODULES_TAB : 'configured'}
        action={!showModules && !showSchedules && canCreate && !creating && !selectedConfiguredId
          ? <ActionButton label={t('setup.heading')} variant="primary" data-split-focus-key="create" onClick={() => setCreating(true)} />
          : undefined} />

      {showSchedules ? (
        <SchedulesOverview configuredLogic={configuredLogic} assets={assetDirectory.assets} owners={assetDirectory.owners}
          executions={canReadRuns ? executions : undefined} isLoaded={isLoaded} />
      ) : showModules ? (
        <div className="split-panes">
          <section className="panel split-browse" aria-label={t('logic.tabModules')}>
            <PanelHeader title="Logic modules" count={visibleGroups.length} />
            <LogicCatalogControls
              criteria={catalogCriteria}
              onChange={setCatalogCriteria}
              runtimes={runtimes}
              matchingCount={visibleModules.length}
              totalCount={modules.length}
            />
            <ModulePackageUpload canManage={permissions.includes('logic.manage')} onUploaded={async module => {
              await onRefresh(); setAboutModuleId(module.id ?? null);
            }} />
            <div className="table-wrap">
              <table className="activity-table">
                <thead>
                  <tr>
                    <th>Status</th>
                    <th>Module</th>
                    <th className="run-col-secondary">Publisher</th>
                    <th className="run-col-secondary">Runtime</th>
                  </tr>
                </thead>
                <tbody>
                  {visibleGroups.map(group => {
                    const selectedFamily = selectedModuleVersions.some(version => version.id === group.versions[0].id);
                    const module = group.versions.find(version => version.id === aboutModuleId) ?? group.versions[0];
                    const allVersions = moduleGroups.find(item => item.key === group.key)?.versions ?? group.versions;
                    return <tr key={group.key} data-selected={selectedFamily}
                      onClick={event => {
                        // The row is a convenience; the name button stays the keyboard and screen-reader route.
                        if ((event.target as HTMLElement).closest('button, a')) return;
                        if (module.id) setAboutModuleId(module.id);
                      }}>
                      <td><Status value={moduleStateToken(module)} label={moduleStateLabel(module)} /><small>{t('moduleGroup.shown', { version: module.version ?? '—' })}</small></td>
                      <td>
                        <button type="button" className="asset-select"
                          aria-expanded={selectedFamily}
                          aria-label={t('modulePanel.about', { name: module.displayName ?? '—' })}
                          onClick={() => setAboutModuleId(selectedFamily ? null : module.id ?? null)}>
                          {module.displayName}
                        </button>
                        <small>{module.moduleId}</small>
                        <small>{t('moduleGroup.versions', { count: allVersions.length })}</small>
                        {group.versions.length < allVersions.length && <small>{t('moduleGroup.matches', { count: group.versions.length })}</small>}
                      </td>
                      <td className="run-col-secondary">{module.publisher}</td>
                      <td className="run-col-secondary">{module.runtime} · {profileLabel(module.executionProfile)}</td>
                    </tr>;
                  })}
                  {visibleModules.length === 0 && (
                    <EmptyRow columns={4} loading={!isLoaded}
                      hint={isLoaded && modules.length === 0 ? t('logicCatalog.noneHint') : undefined}
                      action={isLoaded && modules.length > 0 ? <ActionButton label={t('views.clearFilters')} onClick={() => setCatalogCriteria(DEFAULT_LOGIC_CATALOG_CRITERIA)} /> : undefined}>
                      {!isLoaded ? t('views.loading') : modules.length === 0 ? t('logicCatalog.none') : t('logicCatalog.noMatches')}
                    </EmptyRow>
                  )}
                </tbody>
              </table>
            </div>
          </section>
          <section className="split-selected" tabIndex={-1} aria-label={t('logic.tabModules')}>
            <ActionButton label={t('logic.back')} className="split-back" onClick={() => setAboutModuleId(null)} />
            {aboutModule ? <ModulePanel key={aboutModule.id} module={aboutModule}
              versions={selectedModuleVersions} onSelectVersion={setAboutModuleId}
              configurations={canReadConfigurations ? configuredLogic : []}
              canManage={permissions.includes('logic.manage')} onChanged={onRefresh}
              onClose={() => setAboutModuleId(null)} />
              : <p className="split-note">{t('logic.pickModule')}</p>}
          </section>
        </div>
      ) : (
        <div className="split-panes">
          <section className="panel split-browse" aria-label={t('logic.tabConfigured')}>
            <PanelHeader title="Configured logic"
              count={visibleConfigurations.length} />
            <ConfiguredLogicControls
              criteria={configuredCriteria}
              onChange={setConfiguredCriteria}
              matchingCount={visibleConfigurations.length}
              totalCount={configuredLogic.length}
            />
            <div className="table-wrap">
              <table className="activity-table">
                <thead>
                  <tr>
                    <th>Status</th>
                    <th>Logic</th>
                    <th className="run-col-secondary">Revision</th>
                    {canReadRuns && <th className="run-col-secondary">Last run</th>}
                  </tr>
                </thead>
                <tbody>
                  {visibleConfigurations.map(item => (
                    <tr key={item.id} data-selected={selectedConfiguredId === item.id}
                      onClick={event => {
                        if ((event.target as HTMLElement).closest('button, a')) return;
                        if (item.id) { setCreating(false); setSelectedConfiguredId(item.id); }
                      }}>
                      <td><Status value={listedStatus(item)} /></td>
                      <td>
                        <button type="button" className="asset-select" disabled={!item.id}
                          aria-expanded={selectedConfiguredId === item.id}
                          aria-controls={selectedConfiguredId === item.id ? 'configured-revisions' : undefined}
                          aria-label={t('configured.inspectFor', { name: item.name ?? '—' })}
                          onClick={() => { setCreating(false); setSelectedConfiguredId(current =>
                            current === item.id ? null : item.id ?? null); }}>
                          {item.name}
                        </button>
                        <small>{configuredTargetLabel((contextRevision(item)?.assetBindings ?? []).flatMap(binding => binding.assetId ? [binding.assetId] : []), assetDirectory.assets, assetDirectory.owners)}</small>
                        <small>{contextRevision(item)?.moduleId} · v{contextRevision(item)?.moduleVersion}</small>
                        {(item.revisions ?? []).some(revision => revision.status === 'Draft') && <small>{t('logic.rowDrafts', { count: (item.revisions ?? []).filter(revision => revision.status === 'Draft').length })}</small>}
                      </td>
                      <td className="run-col-secondary">{activeRevisionLabel(item)}</td>
                      {canReadRuns && <td className="run-col-secondary">{lastRunText(lastRuns.get(item.id ?? ''))}</td>}
                    </tr>
                  ))}
                  {visibleConfigurations.length === 0 && (
                    <EmptyRow columns={canReadRuns ? 4 : 3} loading={!isLoaded}
                      hint={isLoaded && configuredLogic.length === 0 ? t('configured.noneHint') : undefined}
                      action={isLoaded && configuredLogic.length > 0 ? <ActionButton label={t('views.clearFilters')} onClick={() => setConfiguredCriteria(DEFAULT_CONFIGURED_LOGIC_CRITERIA)} /> : undefined}>
                      {!isLoaded ? t('views.loading') : configuredLogic.length === 0 ? t('configured.none') : t('configured.noMatches')}
                    </EmptyRow>
                  )}
                </tbody>
              </table>
            </div>
          </section>
          <section className="split-selected" tabIndex={-1} aria-label={t('logic.tabConfigured')}>
            <ActionButton label={t('logic.back')} className="split-back" onClick={() => { setSelectedConfiguredId(null); setCreating(false); }} />
            {hasConfigurationDetail ? (
              <ConfiguredLogicWorkspace
                key={selectedConfiguredId ? JSON.stringify([userId, selectedConfiguredId]) : 'new:' + (startViewId ?? prefillAssetId ?? '') + assets.map(item => item.id).join(',')}
                selectedId={selectedConfiguredId} initialAssetId={prefillAssetId} initialViewId={startViewId}
                canManage={creating ? canManage : canManage && (selectedConfiguration?.canManage ?? true)}
                userId={userId} canRequestShadow={permissions.includes('executions.request') && (selectedConfiguration?.canRequestShadow ?? true)}
                canReadViews={permissions.includes('connections.read')}
                canReadControl={permissions.includes('recommendations.read')}
                canManageControl={permissions.includes(CONTROL_MANAGE_PERMISSION)}
                modules={modules} assets={assetDirectory.assets} owners={assetDirectory.owners} assetTypes={assetTypes}
                executions={canReadRuns ? executions : undefined}
                onChanged={onRefresh}
                onClose={() => { setSelectedConfiguredId(null); setCreating(false); }}
              />
            ) : <p className="split-note">{t('logic.pickConfigured')}</p>}
          </section>
        </div>
      )}
    </div>
  );
}

function contextRevision(item: ConfiguredLogicResponse) {
  return item.revisions?.find(revision => revision.id === item.activeRevisionId) ?? orderedRevisions(item)[0];
}

function activeRevisionLabel(item: ConfiguredLogicResponse): string {
  if (!item.activeRevisionId) return '—';
  const active = item.revisions?.find(revision => revision.id === item.activeRevisionId);
  return active?.revisionNumber ? t('setup.revision', { number: active.revisionNumber }) : '—';
}

function lastRunText(run?: { requestedAtUtc: string; status: string | null }): string {
  return run ? `${formatTimestamp(run.requestedAtUtc)} · ${statusLabel(run.status)}` : t('configured.noRecentRun');
}
