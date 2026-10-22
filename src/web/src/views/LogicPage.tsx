import { useEffect, useMemo, useState } from 'react';
import { hashFor, useForgetVanishedSelection, useHashDetail } from '../routing';
import type {
  AssetResponse,
  AssetTypeResponse,
  ConfiguredLogicResponse,
  ExecutionResponse,
  LogicModuleResponse,
} from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { PanelHeader, StatusPill } from '../components/common';
import { formatTimestamp } from '../format';
import { profileLabel, statusLabel } from '../words';
import { moduleStateLabel, moduleStateToken } from './moduleApproval';
import { configurationAssetLabels, latestRunByConfiguration, summarizeAssetLabels } from './runContext';
import { ConfiguredLogicControls } from './ConfiguredLogicControls';
import { ConfiguredLogicWorkspace } from './ConfiguredLogicWorkspace';
import { ModulePanel } from './ModulePanel';
import { LogicCatalogControls } from './LogicCatalogControls';
import { DEFAULT_CONFIGURED_LOGIC_CRITERIA, filterConfiguredLogic, latestRevision } from './configuredLogicViews';
import type { ConfiguredLogicCriteria } from './configuredLogicViews';
import { DEFAULT_LOGIC_CATALOG_CRITERIA, filterLogicCatalog, availableLogicRuntimes } from './logicCatalog';
import type { LogicCatalogCriteria } from './logicCatalog';
import './splitLayout.css';
import { LoadingIndicator } from '../components/LoadingIndicator';

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

  const hasConfigurationDetail = Boolean(selectedConfiguration) || (creating && canCreate);

  return (
    <div className="split-layout" data-has-selection={showModules ? Boolean(aboutModule) : hasConfigurationDetail}>
      <div className="split-tabs" role="tablist" aria-label={t('logic.tabs')}>
        {canReadConfigurations && <a role="tab" href={hashFor('logic')} aria-selected={!showModules}>
          {t('logic.tabConfigured')} <span>{configuredLogic.length}</span>
        </a>}
        {canReadCatalog && <a role="tab" href={hashFor('logic', MODULES_TAB)} aria-selected={showModules}>
          {t('logic.tabModules')} <span>{modules.length}</span>
        </a>}
        {!showModules && canCreate && !creating && !selectedConfiguredId && <button type="button"
          className="primary-button split-add" onClick={() => setCreating(true)}>{t('setup.heading')}</button>}
      </div>

      {showModules ? (
        <div className="split-panes">
          <section className="panel split-browse" aria-label={t('logic.tabModules')}>
            <PanelHeader eyebrow="Catalog" title="Logic Modules" count={visibleModules.length} />
            <LogicCatalogControls
              criteria={catalogCriteria}
              onChange={setCatalogCriteria}
              runtimes={runtimes}
              matchingCount={visibleModules.length}
              totalCount={modules.length}
            />
            {visibleModules.length === 0 ? (
              <>
                {!isLoaded ? <LoadingIndicator label={t('views.loading')} />
                  : <p className="split-note">{modules.length === 0 ? t('logicCatalog.none') : t('logicCatalog.noMatches')}</p>}
                {isLoaded && modules.length === 0 && <p className="split-note">{t('logicCatalog.noneHint')}</p>}
              </>
            ) : (
              <ul className="split-results">
                {visibleModules.map(module => (
                  <li key={module.id} data-selected={aboutModuleId === module.id}>
                    <button type="button" className="asset-select"
                      aria-expanded={aboutModuleId === module.id}
                      aria-label={t('modulePanel.about', { name: module.displayName ?? '—' })}
                      onClick={() => setAboutModuleId(current => (current === module.id ? null : module.id ?? null))}>
                      {module.displayName}
                    </button>
                    <small>{module.moduleId} · v{module.version} · {module.publisher} · {module.runtime} · {profileLabel(module.executionProfile)}</small>
                    <small><StatusPill value={moduleStateToken(module)} label={moduleStateLabel(module)} /></small>
                  </li>
                ))}
              </ul>
            )}
          </section>
          <section className="split-selected" aria-label={t('logic.tabModules')}>
            <button type="button" className="secondary-button split-back"
              onClick={() => setAboutModuleId(null)}>{t('logic.back')}</button>
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
            <PanelHeader eyebrow="Configuration" title="Configured Logic"
              count={visibleConfigurations.length} />
            <ConfiguredLogicControls
              criteria={configuredCriteria}
              onChange={setConfiguredCriteria}
              matchingCount={visibleConfigurations.length}
              totalCount={configuredLogic.length}
            />
            {visibleConfigurations.length === 0 ? (
              <>
                {!isLoaded ? <LoadingIndicator label={t('views.loading')} />
                  : <p className="split-note">{configuredLogic.length === 0 ? t('configured.none') : t('configured.noMatches')}</p>}
                {isLoaded && configuredLogic.length === 0 && <p className="split-note">{t('configured.noneHint')}</p>}
              </>
            ) : (
              <ul className="split-results">
                {visibleConfigurations.map(item => {
                  const latest = latestRevision(item);
                  return (
                    <li key={item.id} data-selected={selectedConfiguredId === item.id}>
                      <button type="button" className="asset-select" disabled={!item.id}
                        aria-expanded={selectedConfiguredId === item.id}
                        aria-controls={selectedConfiguredId === item.id ? 'configured-revisions' : undefined}
                        aria-label={t('configured.inspectFor', { name: item.name ?? '—' })}
                        onClick={() => { setCreating(false); setSelectedConfiguredId(current =>
                          current === item.id ? null : item.id ?? null); }}>
                        {item.name}
                      </button>
                      <small>
                        {summarizeAssetLabels(configurationAssetLabels(item, assets)) || '—'}
                        {' · '}{activeRevisionLabel(item)}
                        {canReadRuns && ' · ' + lastRunText(lastRuns.get(item.id ?? ''))}
                      </small>
                      <small><StatusPill value={latest?.status ?? 'Draft'} /></small>
                    </li>
                  );
                })}
              </ul>
            )}
          </section>
          <section className="split-selected" aria-label={t('logic.tabConfigured')}>
            <button type="button" className="secondary-button split-back"
              onClick={() => { setSelectedConfiguredId(null); setCreating(false); }}>{t('logic.back')}</button>
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
