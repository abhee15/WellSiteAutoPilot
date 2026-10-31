import { useSplitPaneFocus } from '../components/useSplitPaneFocus';
import { useEffect, useMemo, useState } from 'react';
import { ActionButton } from '../components/ActionButton';
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
import { configurationAssetLabels, latestRunByConfiguration, summarizeAssetLabels } from './runContext';
import { ConfiguredLogicControls } from './ConfiguredLogicControls';
import { ConfiguredLogicWorkspace } from './ConfiguredLogicWorkspace';
import { ModulePanel } from './ModulePanel';
import { ModulePackageUpload } from './ModulePackageUpload';
import { LogicCatalogControls } from './LogicCatalogControls';
import { DEFAULT_CONFIGURED_LOGIC_CRITERIA, filterConfiguredLogic, listedStatus } from './configuredLogicViews';
import type { ConfiguredLogicCriteria } from './configuredLogicViews';
import { DEFAULT_LOGIC_CATALOG_CRITERIA, filterLogicCatalog, availableLogicRuntimes } from './logicCatalog';
import type { LogicCatalogCriteria } from './logicCatalog';
import './splitLayout.css';

/** `#logic/modules` opens the Modules tab. */
const MODULES_TAB = 'modules';

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
  const startAssetId = rawDetail?.startsWith('new:') ? rawDetail.slice(4) : null;
  const modulesRequested = rawDetail === MODULES_TAB;
  const selectedConfiguredId = startAssetId || modulesRequested ? null : rawDetail;
  const showModules = canReadCatalog && (modulesRequested || !canReadConfigurations);
  const [creating, setCreating] = useState(Boolean(startAssetId));
  useEffect(() => { if (startAssetId) setCreating(true); }, [startAssetId]);
  const prefillAssetId = startAssetId && assets.some(asset => asset.id === startAssetId && asset.isActive)
    ? startAssetId : null;
  const [aboutModuleId, setAboutModuleId] = useState<string | null>(null);
  const aboutModule = modules.find(module => module.id === aboutModuleId);
  const canManage = mayManage && canReadConfigurations;
  const canCreate = canManage && assets.some(asset => asset.isActive);
  useEffect(() => { if (!canCreate) setCreating(false); }, [canCreate]);
  const selectedConfiguration = configuredLogic.find(item => item.id === selectedConfiguredId);
  useForgetVanishedSelection(selectedConfiguredId, Boolean(selectedConfiguration), () => setSelectedConfiguredId(null));
  const visibleConfigurations = useMemo(
    () => filterConfiguredLogic(configuredLogic, configuredCriteria),
    [configuredLogic, configuredCriteria],
  );
  const visibleModules = useMemo(
    () => filterLogicCatalog(modules, catalogCriteria),
    [modules, catalogCriteria],
  );
  const runtimes = useMemo(() => availableLogicRuntimes(modules), [modules]);

  const splitFocus = useSplitPaneFocus(showModules ? aboutModule?.id ?? null
    : selectedConfiguration?.id ?? (creating && canCreate ? 'new' : null));
  const hasConfigurationDetail = Boolean(selectedConfiguration) || (creating && canCreate);

  return (
    <div {...splitFocus} className="split-layout runs-layout logic-layout" data-has-selection={showModules ? Boolean(aboutModule) : hasConfigurationDetail}>
      <div className="split-tabs" role="tablist" aria-label={t('logic.tabs')}>
        {canReadConfigurations && <a role="tab" href={hashFor('logic')} aria-selected={!showModules}>
          {t('logic.tabConfigured')} <span>{configuredLogic.length}</span>
        </a>}
        {canReadCatalog && <a role="tab" href={hashFor('logic', MODULES_TAB)} aria-selected={showModules}>
          {t('logic.tabModules')} <span>{modules.length}</span>
        </a>}
        {!showModules && canCreate && !creating && !selectedConfiguredId && <ActionButton label={t('setup.heading')} variant="primary" className="split-add" data-split-focus-key="create" onClick={() => setCreating(true)} />}
      </div>

      {showModules ? (
        <div className="split-panes">
          <section className="panel split-browse" aria-label={t('logic.tabModules')}>
            <PanelHeader title="Logic Modules" count={visibleModules.length} />
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
                  {visibleModules.map(module => (
                    <tr key={module.id} data-selected={aboutModuleId === module.id}
                      onClick={event => {
                        // The row is a convenience; the name button stays the keyboard and screen-reader route.
                        if ((event.target as HTMLElement).closest('button, a')) return;
                        if (module.id) setAboutModuleId(module.id);
                      }}>
                      <td><Status value={moduleStateToken(module)} label={moduleStateLabel(module)} /></td>
                      <td>
                        <button type="button" className="asset-select"
                          aria-expanded={aboutModuleId === module.id}
                          aria-label={t('modulePanel.about', { name: module.displayName ?? '—' })}
                          onClick={() => setAboutModuleId(current => (current === module.id ? null : module.id ?? null))}>
                          {module.displayName}
                        </button>
                        <small>{module.moduleId} · v{module.version}</small>
                      </td>
                      <td className="run-col-secondary">{module.publisher}</td>
                      <td className="run-col-secondary">{module.runtime} · {profileLabel(module.executionProfile)}</td>
                    </tr>
                  ))}
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
            {aboutModule ? <ModulePanel module={aboutModule}
              configurations={canReadConfigurations ? configuredLogic : []}
              canManage={permissions.includes('logic.manage')} onChanged={onRefresh}
              onClose={() => setAboutModuleId(null)} />
              : <p className="split-note">{t('logic.pickModule')}</p>}
          </section>
        </div>
      ) : (
        <div className="split-panes">
          <section className="panel split-browse" aria-label={t('logic.tabConfigured')}>
            <PanelHeader title="Configured Logic"
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
                    <th className="run-col-secondary">Assets</th>
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
                      </td>
                      <td className="run-col-secondary">{summarizeAssetLabels(configurationAssetLabels(item, assets)) || '—'}</td>
                      <td className="run-col-secondary">{activeRevisionLabel(item)}</td>
                      {canReadRuns && <td className="run-col-secondary">{lastRunText(lastRuns.get(item.id ?? ''))}</td>}
                    </tr>
                  ))}
                  {visibleConfigurations.length === 0 && (
                    <EmptyRow columns={canReadRuns ? 5 : 4} loading={!isLoaded}
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
                key={selectedConfiguredId ? JSON.stringify([userId, selectedConfiguration]) : 'new:' + (prefillAssetId ?? '') + assets.map(item => item.id).join(',')}
                selectedId={selectedConfiguredId} initialAssetId={prefillAssetId}
                canManage={creating ? canManage : canManage && selectedConfiguration?.canManage === true}
                userId={userId} canRequestShadow={permissions.includes('executions.request') && selectedConfiguration?.canRequestShadow === true}
                canReadViews={permissions.includes('connections.read')}
                modules={modules} assets={assets} assetTypes={assetTypes}
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

function activeRevisionLabel(item: ConfiguredLogicResponse): string {
  if (!item.activeRevisionId) return '—';
  const active = item.revisions?.find(revision => revision.id === item.activeRevisionId);
  return active?.revisionNumber ? t('setup.revision', { number: active.revisionNumber }) : '—';
}

function lastRunText(run?: { requestedAtUtc: string; status: string | null }): string {
  return run ? `${formatTimestamp(run.requestedAtUtc)} · ${statusLabel(run.status)}` : t('configured.noRecentRun');
}
