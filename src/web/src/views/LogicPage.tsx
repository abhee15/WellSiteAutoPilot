import { DataTable } from '../components/DataTable';
import { PanelLoading } from '../components/LoadingIndicator';
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
import { profileLabel, runtimeLabel } from '../words';
import { moduleStateLabel, moduleStateToken } from './moduleApproval';
import { configurationTargetAssetIds } from './runContext';
import { useAssetIdentityDirectory } from './useAssetIdentityDirectory';
import { ConfiguredLogicControls } from './ConfiguredLogicControls';
import { ConfiguredLogicWorkspace } from './ConfiguredLogicWorkspace';
import { CONTROL_MANAGE_PERMISSION } from './controlExecution';
import { ModulePanel } from './ModulePanel';
import { SchedulesOverview } from './SchedulesOverview';
import { ModulePackageUpload } from './ModulePackageUpload';
import { LogicCatalogControls } from './LogicCatalogControls';
import { DEFAULT_CONFIGURED_LOGIC_CRITERIA, filterConfiguredLogic, groupConfigurations } from './configuredLogicViews';
import { ConfigurationList } from './ConfigurationList';
import type { ConfiguredLogicCriteria } from './configuredLogicViews';
import { DEFAULT_LOGIC_CATALOG_CRITERIA, filterLogicCatalog, availableLogicRuntimes, groupModuleVersions } from './logicCatalog';
import type { LogicCatalogCriteria } from './logicCatalog';
import './splitLayout.css';

/** `#logic/modules` opens the Modules tab and `#logic/schedules` the Schedules tab. */
const MODULES_TAB = 'modules';
const SCHEDULES_TAB = 'schedules';
const MODULE_PREFIX = 'modules:';

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
  const mayManage = permissions.includes('configured-logic.manage');
  const [catalogCriteria, setCatalogCriteria] = useState<LogicCatalogCriteria>(
    DEFAULT_LOGIC_CATALOG_CRITERIA,
  );
  const [configuredCriteria, setConfiguredCriteria] = useState<ConfiguredLogicCriteria>(
    DEFAULT_CONFIGURED_LOGIC_CRITERIA,
  );
  const [rawDetail, setSelectedConfiguredId] = useHashDetail('logic');
  // `#logic/new:<assetId>` starts setup from an asset (the "Put logic on this asset" link);
  // `#logic/modules` opens the catalog and `modules:<id>` opens an installed version.
  const startViewId = rawDetail?.startsWith('new-view:') ? rawDetail.slice(9) : null;
  const startAssetId = rawDetail?.startsWith('new:') ? rawDetail.slice(4) : null;
  const aboutModuleId = rawDetail?.startsWith(MODULE_PREFIX) ? rawDetail.slice(MODULE_PREFIX.length) : null;
  const modulesRequested = rawDetail === MODULES_TAB || aboutModuleId !== null;
  const schedulesRequested = rawDetail === SCHEDULES_TAB;
  const selectedConfiguredId = startAssetId || startViewId || modulesRequested || schedulesRequested ? null : rawDetail;
  const showSchedules = canReadConfigurations && schedulesRequested;
  const showModules = !showSchedules && canReadCatalog && (modulesRequested || !canReadConfigurations);
  const [creating, setCreating] = useState(Boolean(startAssetId || startViewId));
  const [activityFocused, setActivityFocused] = useState(false);
  useEffect(() => { if (startAssetId || startViewId) setCreating(true); }, [startAssetId, startViewId]);
  const prefillAssetId = startAssetId && assets.some(asset => asset.id === startAssetId && asset.isActive)
    ? startAssetId : null;
  const setAboutModuleId = (id: string | null) => setSelectedConfiguredId(id ? MODULE_PREFIX + id : MODULES_TAB);
  const aboutModule = canReadCatalog ? modules.find(module => module.id === aboutModuleId) : undefined;
  const canManage = mayManage && canReadConfigurations;
  const canCreate = canManage && (Boolean(startViewId) || assets.some(asset => asset.isActive));
  useEffect(() => { if (!canCreate) setCreating(false); }, [canCreate]);
  const selectedConfiguration = configuredLogic.find(item => item.id === selectedConfiguredId);
  useForgetVanishedSelection(selectedConfiguredId, Boolean(selectedConfiguration), () => setSelectedConfiguredId(null));
  const moduleGroups = useMemo(() => groupModuleVersions(canReadCatalog ? modules : []), [canReadCatalog, modules]);
  const moduleCatalog = useMemo(() => new Map(moduleGroups.map(group => [group.key, group.versions])), [moduleGroups]);
  const moduleNames = useMemo(() => new Map(moduleGroups.map(group =>
    [group.key, group.versions.find(version => version.displayName)?.displayName ?? group.key])), [moduleGroups]);
  const visibleConfigurations = useMemo(
    () => filterConfiguredLogic(canReadConfigurations ? configuredLogic : [], configuredCriteria, moduleNames),
    [canReadConfigurations, configuredLogic, configuredCriteria, moduleNames],
  );
  const configurationGroups = useMemo(
    () => {
      const groups = groupConfigurations(visibleConfigurations, moduleCatalog);
      return configuredCriteria.sort === 'name' ? groups.sort((a, b) => a.name.localeCompare(b.name)) : groups;
    },
    [visibleConfigurations, moduleCatalog, configuredCriteria.sort],
  );
  // Resolve visible names independently from the selected detail, including the Schedules overview.
  const gridTargetIds = useMemo(() => {
    if (showModules) return [];
    const items = showSchedules ? configuredLogic : visibleConfigurations;
    const targets = items.flatMap(item => configurationTargetAssetIds(item).slice(0, 2));
    return [...new Set(targets)].slice(0, 100);
  }, [showModules, showSchedules, configuredLogic, visibleConfigurations]);
  const canReadAssets = permissions.includes('assets.read');
  const gridAssetDirectory = useAssetIdentityDirectory(gridTargetIds, assets, canReadAssets, userId);
  // A selected configuration may reference historical assets not shown in the grid.
  // Keep the lookup bounded and scoped to the detail, reusing already resolved grid names.
  const detailTargetIds = useMemo(() => {
    if (showModules) return [];
    const targets = (selectedConfiguration?.revisions ?? []).flatMap(revision =>
      (revision.assetBindings ?? []).flatMap(binding => binding.assetId ? [binding.assetId] : []));
    return [...new Set(targets)].slice(0, 40);
  }, [showModules, selectedConfiguration]);
  const detailAssetDirectory = useAssetIdentityDirectory(
    detailTargetIds, gridAssetDirectory.assets, canReadAssets, userId,
  );
  const visibleModules = useMemo(
    () => filterLogicCatalog(modules, catalogCriteria),
    [modules, catalogCriteria],
  );
  const visibleGroups = useMemo(() => groupModuleVersions(visibleModules), [visibleModules]);
  const selectedModuleVersions = moduleGroups.find(group => group.versions.some(version => version.id === aboutModuleId))?.versions ?? [];
  const runtimes = useMemo(() => availableLogicRuntimes(modules), [modules]);

  const configurationSelection = canReadConfigurations ? selectedConfiguredId : null;
  const splitFocus = useSplitPaneFocus(showModules ? aboutModuleId
    : configurationSelection ?? (creating && canCreate ? 'new' : null));
  const hasConfigurationDetail = configurationSelection !== null || (creating && canCreate);
  const isActivityFocused = activityFocused && selectedConfiguredId !== null && !showSchedules && !showModules;

  return (
    <div {...splitFocus} className={`split-layout runs-layout logic-layout${isActivityFocused ? ' logic-history-focused' : ''}`}
      data-has-selection={showSchedules ? false : showModules ? Boolean(aboutModuleId) : hasConfigurationDetail}>
      <PageTabNav label={t('logic.tabs')} compactOnNarrow
        tabs={[
          ...(canReadConfigurations ? [
            { id: 'configured', label: t('logic.tabConfigured'), href: hashFor('logic'), count: configuredLogic.length },
          ] : []),
          ...(canReadCatalog ? [
            { id: MODULES_TAB, label: t('logic.tabModules'), href: hashFor('logic', MODULES_TAB), count: moduleGroups.length },
          ] : []),
          ...(canReadConfigurations ? [
            { id: SCHEDULES_TAB, label: t('logic.tabSchedules'), href: hashFor('logic', SCHEDULES_TAB) },
          ] : []),
        ]}
        activeId={showSchedules ? SCHEDULES_TAB : showModules ? MODULES_TAB : 'configured'}
        action={!showModules && !showSchedules && canCreate && !creating && !selectedConfiguredId
          ? <ActionButton label={t('setup.heading')} variant="primary" data-split-focus-key="create" onClick={() => setCreating(true)} />
          : undefined} />

      {showSchedules ? (
        <SchedulesOverview configuredLogic={configuredLogic} assets={gridAssetDirectory.assets} owners={gridAssetDirectory.owners}
          executions={canReadRuns ? executions : undefined} isLoaded={isLoaded} />
      ) : showModules ? (
        <div className="split-panes">
          <section className="panel split-browse" aria-label={t('logic.tabModules')}>
            <PanelHeader title={t('logicCatalog.tableLabel')} count={visibleGroups.length} />
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
            {!isLoaded ? <PanelLoading label={t('views.loading')} /> : <DataTable label={t('logicCatalog.tableLabel')}>
                <thead>
                  <tr>
                    <th>{t('logicCatalog.colModule')}</th>
                    <th>{t('logicCatalog.colStatus')}</th>
                    <th className="run-col-secondary">{t('logicCatalog.colPublisher')}</th>
                    <th className="run-col-secondary">{t('logicCatalog.colRuntime')}</th>
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
                      <td><Status value={moduleStateToken(module)} label={moduleStateLabel(module)} /><small>{t('moduleGroup.shown', { version: module.version ?? '—' })}</small></td>
                      <td className="run-col-secondary">{module.publisher}</td>
                      <td className="run-col-secondary">{runtimeLabel(module.runtime)} · {profileLabel(module.executionProfile)}</td>
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
              </DataTable>}
          </section>
          <section className="split-selected" tabIndex={-1} aria-label={t('logic.tabModules')}>
            <ActionButton label={t('logic.back')} className="split-back" onClick={() => setAboutModuleId(null)} />
            {aboutModule ? <ModulePanel key={aboutModule.id} module={aboutModule}
              versions={selectedModuleVersions} onSelectVersion={setAboutModuleId}
              configurations={canReadConfigurations ? configuredLogic : undefined}
              canManage={permissions.includes('logic.manage')} onChanged={onRefresh}
              onClose={() => setAboutModuleId(null)} />
              : <p className="split-note">{t(aboutModuleId ? 'configured.moduleUnavailable' : 'logic.pickModule')}</p>}
          </section>
        </div>
      ) : (
        <div className="split-panes">
          <section className="panel split-browse" aria-label={t('logic.tabConfigured')}>
            <PanelHeader title={t('logic.tabConfigured')}
              count={visibleConfigurations.length} />
            <ConfiguredLogicControls
              criteria={configuredCriteria}
              onChange={setConfiguredCriteria}
              matchingCount={visibleConfigurations.length}
              totalCount={configuredLogic.length}
            />
            <ConfigurationList groups={configurationGroups} selectedId={configurationSelection}
              isLoaded={isLoaded} totalCount={configuredLogic.length} assets={gridAssetDirectory.assets}
              owners={gridAssetDirectory.owners} executions={canReadRuns ? executions : undefined}
              canReadConnections={permissions.includes('connections.read')} userId={userId}
              onClearFilters={() => setConfiguredCriteria(DEFAULT_CONFIGURED_LOGIC_CRITERIA)}
              onSelect={id => { setCreating(false); setSelectedConfiguredId(id); }} />
          </section>
          <section id="configured-revisions" className="split-selected" tabIndex={-1} aria-label={t('logic.tabConfigured')}>
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
                modules={canReadCatalog ? modules : []} assets={detailAssetDirectory.assets} owners={detailAssetDirectory.owners} assetTypes={assetTypes}
                executions={canReadRuns ? executions : undefined}
                onChanged={onRefresh} onActivityFocusedChange={setActivityFocused}
                onClose={() => { setSelectedConfiguredId(null); setCreating(false); }}
              />
            ) : <p className="split-note">{t('logic.pickConfigured')}</p>}
          </section>
        </div>
      )}
    </div>
  );
}
