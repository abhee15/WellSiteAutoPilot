import { useEffect, useMemo, useState } from 'react';
import { useForgetVanishedSelection, useHashDetail } from '../routing';
import type {
  AssetResponse,
  AssetTypeResponse,
  ConfiguredLogicResponse,
  ExecutionResponse,
  LogicModuleResponse,
} from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { EmptyRow, PanelHeader, StatusPill, SummaryCard } from '../components/common';
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
  // `#logic/new:<assetId>` starts setup from an asset (the "Put logic on this asset" link).
  const startAssetId = rawDetail?.startsWith('new:') ? rawDetail.slice(4) : null;
  const selectedConfiguredId = startAssetId ? null : rawDetail;
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
  const trusted = modules.filter(module => module.isEnabled).length;
  const activeConfigurations = configuredLogic.filter(item => item.activeRevisionId).length;

  return (
    <>
      <section className="summary-grid" aria-label="Logic summary">
        {canReadCatalog && <SummaryCard label="Installed modules" value={String(modules.length)} detail="Immutable module versions" />}
        {canReadCatalog && <SummaryCard label="Trusted and enabled" value={String(trusted)} detail="Eligible for configuration" />}
        {canReadConfigurations && <SummaryCard label="Active configurations" value={String(activeConfigurations)} detail="Configured Logic with active revision" />}
      </section>

      {canReadCatalog && <section className="panel">
        <PanelHeader eyebrow="Catalog" title="Logic Modules" count={visibleModules.length} />
        <LogicCatalogControls
          criteria={catalogCriteria}
          onChange={setCatalogCriteria}
          runtimes={runtimes}
          matchingCount={visibleModules.length}
          totalCount={modules.length}
        />
        <div className="table-wrap">
          <table className="activity-table">
            <thead>
              <tr>
                <th>Module</th>
                <th>Publisher</th>
                <th>Runtime</th>
                <th>Profile</th>
                <th>Trust</th>
                <th>Installed</th>
              </tr>
            </thead>
            <tbody>
              {visibleModules.map(module => (
                <tr key={module.id}>
                  <td>
                    <button type="button" className="asset-select"
                      aria-expanded={aboutModuleId === module.id}
                      aria-label={t('modulePanel.about', { name: module.displayName ?? '—' })}
                      onClick={() => setAboutModuleId(current => (current === module.id ? null : module.id ?? null))}>
                      {module.displayName}
                    </button>
                    <small>{module.moduleId} · v{module.version}</small>
                  </td>
                  <td>{module.publisher}</td>
                  <td>{module.runtime}</td>
                  <td>{profileLabel(module.executionProfile)}</td>
                  <td><StatusPill value={moduleStateToken(module)} label={moduleStateLabel(module)} /></td>
                  <td>{formatTimestamp(module.installedAtUtc)}</td>
                </tr>
              ))}
              {visibleModules.length === 0 && (
                <EmptyRow columns={6} hint={isLoaded && modules.length === 0 ? t('logicCatalog.noneHint') : undefined}>
                  {!isLoaded ? t('views.loading') : modules.length === 0 ? t('logicCatalog.none') : t('logicCatalog.noMatches')}
                </EmptyRow>
              )}
            </tbody>
          </table>
        </div>
        {aboutModule && <ModulePanel module={aboutModule}
          configurations={canReadConfigurations ? configuredLogic : []}
          canManage={permissions.includes('logic.manage')} onChanged={onRefresh}
          onClose={() => setAboutModuleId(null)} />}
      </section>}

      {canReadConfigurations && <section className="panel">
        <PanelHeader eyebrow="Configuration" title="Configured Logic"
          count={visibleConfigurations.length} />
        {canCreate && !creating && !selectedConfiguredId && <button type="button" className="primary-button"
          onClick={() => setCreating(true)}>{t('setup.heading')}</button>}
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
                <th>Name</th>
                <th>{t('configured.assetsHeading')}</th>
                <th>Active revision</th>
                <th>Revisions</th>
                <th>Latest status</th>
                {canReadRuns && <th>{t('configured.lastRun')}</th>}
                <th>Created</th>
                <th>{t('configured.revisionsHeading')}</th>
              </tr>
            </thead>
            <tbody>
              {visibleConfigurations.map(item => {
                const latest = latestRevision(item);

                return (
                  <tr key={item.id}>
                    <td><strong>{item.name}</strong></td>
                    <td>{summarizeAssetLabels(configurationAssetLabels(item, assets)) || '—'}</td>
                    <td>{activeRevisionLabel(item)}</td>
                    <td>{item.revisions?.length ?? 0}</td>
                    <td><StatusPill value={latest?.status ?? 'Draft'} /></td>
                    {canReadRuns && <td>{lastRunText(lastRuns.get(item.id ?? ''))}</td>}
                    <td>{formatTimestamp(item.createdAtUtc)}</td>
                    <td>
                      <button
                        type="button"
                        className="execution-inspect-button"
                        disabled={!item.id}
                        aria-expanded={selectedConfiguredId === item.id}
                        aria-controls={selectedConfiguredId === item.id
                          ? 'configured-revisions' : undefined}
                        aria-label={t('configured.inspectFor', { name: item.name ?? '—' })}
                        onClick={() => { setCreating(false); setSelectedConfiguredId(current =>
                          current === item.id ? null : item.id ?? null); }}
                      >
                        {t('configured.inspect')}
                      </button>
                    </td>
                  </tr>
                );
              })}
              {visibleConfigurations.length === 0 && (
                <EmptyRow columns={canReadRuns ? 8 : 7}
                  hint={isLoaded && configuredLogic.length === 0 ? t('configured.noneHint') : undefined}>
                  {!isLoaded ? t('views.loading') : configuredLogic.length === 0 ? t('configured.none') : t('configured.noMatches')}
                </EmptyRow>
              )}
            </tbody>
          </table>
        </div>
        {((creating && canCreate) || selectedConfiguration) && (
          <ConfiguredLogicWorkspace
            key={selectedConfiguredId ? JSON.stringify([userId, selectedConfiguration]) : 'new:' + (prefillAssetId ?? '') + assets.map(item => item.id).join(',')}
            selectedId={selectedConfiguredId} initialAssetId={prefillAssetId}
            canManage={creating ? canManage : canManage && selectedConfiguration?.canManage === true}
            userId={userId} canRequestShadow={permissions.includes('executions.request') && selectedConfiguration?.canRequestShadow === true}
            modules={modules} assets={assets} assetTypes={assetTypes}
            executions={canReadRuns ? executions : undefined}
            onChanged={onRefresh}
            onClose={() => { setSelectedConfiguredId(null); setCreating(false); }}
          />
        )}
      </section>}
    </>
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
