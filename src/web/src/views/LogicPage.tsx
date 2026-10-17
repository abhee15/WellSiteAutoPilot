import { useEffect, useMemo, useState } from 'react';
import { useForgetVanishedSelection, useHashDetail } from '../routing';
import type {
  AssetResponse,
  AssetTypeResponse,
  ConfiguredLogicResponse,
  LogicModuleResponse,
} from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { EmptyRow, PanelHeader, StatusPill, SummaryCard } from '../components/common';
import { formatTimestamp, shortId } from '../format';
import { ConfiguredLogicControls } from './ConfiguredLogicControls';
import { ConfiguredLogicWorkspace } from './ConfiguredLogicWorkspace';
import { LogicCatalogControls } from './LogicCatalogControls';
import { DEFAULT_CONFIGURED_LOGIC_CRITERIA, filterConfiguredLogic, latestRevision } from './configuredLogicViews';
import type { ConfiguredLogicCriteria } from './configuredLogicViews';
import { DEFAULT_LOGIC_CATALOG_CRITERIA, filterLogicCatalog, availableLogicRuntimes } from './logicCatalog';
import type { LogicCatalogCriteria } from './logicCatalog';

export function LogicPage({
  modules,
  configuredLogic,
  userId, assets, assetTypes, onRefresh, permissions,
}: {
  modules: LogicModuleResponse[];
  configuredLogic: ConfiguredLogicResponse[];
  userId: string | null | undefined;
  assets: AssetResponse[];
  assetTypes: AssetTypeResponse[];
  onRefresh: () => Promise<void>;
  permissions: readonly string[];
}) {
  const canReadCatalog = permissions.includes('logic.read');
  const canReadConfigurations = permissions.includes('configured-logic.read');
  const mayManage = permissions.includes('configured-logic.manage');
  const [catalogCriteria, setCatalogCriteria] = useState<LogicCatalogCriteria>(
    DEFAULT_LOGIC_CATALOG_CRITERIA,
  );
  const [configuredCriteria, setConfiguredCriteria] = useState<ConfiguredLogicCriteria>(
    DEFAULT_CONFIGURED_LOGIC_CRITERIA,
  );
  const [selectedConfiguredId, setSelectedConfiguredId] = useHashDetail('logic');
  const [creating, setCreating] = useState(false);
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
                    <strong>{module.displayName}</strong>
                    <small>{module.moduleId} · v{module.version}</small>
                  </td>
                  <td>{module.publisher}</td>
                  <td>{module.runtime}</td>
                  <td>{module.executionProfile}</td>
                  <td><StatusPill value={module.trustStatus} /></td>
                  <td>{formatTimestamp(module.installedAtUtc)}</td>
                </tr>
              ))}
              {visibleModules.length === 0 && (
                <EmptyRow columns={6}>
                  {modules.length === 0 ? t('logicCatalog.none') : t('logicCatalog.noMatches')}
                </EmptyRow>
              )}
            </tbody>
          </table>
        </div>
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
                <th>Active revision</th>
                <th>Revisions</th>
                <th>Latest status</th>
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
                    <td>{item.activeRevisionId ? shortId(item.activeRevisionId) : '—'}</td>
                    <td>{item.revisions?.length ?? 0}</td>
                    <td><StatusPill value={latest?.status ?? 'Draft'} /></td>
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
                <EmptyRow columns={6}>
                  {configuredLogic.length === 0 ? t('configured.none') : t('configured.noMatches')}
                </EmptyRow>
              )}
            </tbody>
          </table>
        </div>
        {((creating && canCreate) || selectedConfiguration) && (
          <ConfiguredLogicWorkspace
            key={selectedConfiguredId ? JSON.stringify([userId, selectedConfiguration]) : 'new:' + assets.map(item => item.id).join(',')}
            selectedId={selectedConfiguredId}
            canManage={creating ? canManage : canManage && selectedConfiguration?.canManage === true}
            userId={userId} canRequestShadow={permissions.includes('executions.request') && selectedConfiguration?.canRequestShadow === true}
            modules={modules} assets={assets} assetTypes={assetTypes}
            onChanged={onRefresh}
            onClose={() => { setSelectedConfiguredId(null); setCreating(false); }}
          />
        )}
      </section>}
    </>
  );
}
