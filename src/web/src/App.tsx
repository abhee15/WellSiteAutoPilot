import { canOpenSection, isAwaitingAccess, isSignInRequired, loadSession, sessionAccessKey } from './security/sessionAccess';
import { AwaitingAccessNotice } from './components/AwaitingAccessNotice';
import { loadWorkspaceSnapshot } from './views/workspaceSnapshot';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type {
  AssetResponse,
  AssetCreationCapabilitiesResponse,
  AssetTypeResponse,
  ConfiguredLogicResponse,
  CurrentUserResponse,
  ExecutionResponse,
  LogicModuleResponse,
  SystemInfoResponse,
} from './api/generated/v1/types.gen';
import { t } from './localization';
import { UserMenu } from './components/UserMenu';
import { NavigationLink } from './components/NavigationLink';
import type { Section } from './components/NavigationLink';

// Compatibility with existing consumers; new components live in their own files.
export { UserMenu, NavigationLink };
import { OperationsViewControls } from './views/OperationsViewControls';
import { ExecutionDetailPanel } from './views/ExecutionDetailPanel';
import { RecommendationsPage } from './views/RecommendationsPage';
import { AdministrationWorkspace } from './views/AdministrationWorkspace';
import { AssetInventoryControls } from './views/AssetInventoryControls';
import { AssetCreationPanel } from './views/AssetCreationPanel';
import { LogicCatalogControls } from './views/LogicCatalogControls';
import { ConfiguredLogicControls } from './views/ConfiguredLogicControls';
import { ConfiguredLogicWorkspace } from './views/ConfiguredLogicWorkspace';
import { DEFAULT_CONFIGURED_LOGIC_CRITERIA, filterConfiguredLogic, latestRevision } from './views/configuredLogicViews';
import type { ConfiguredLogicCriteria } from './views/configuredLogicViews';
import { DEFAULT_LOGIC_CATALOG_CRITERIA, filterLogicCatalog, availableLogicRuntimes } from './views/logicCatalog';
import type { LogicCatalogCriteria } from './views/logicCatalog';
import { DEFAULT_ASSET_INVENTORY_CRITERIA, filterAssetInventory } from './views/assetInventory';
import type { AssetInventoryCriteria } from './views/assetInventory';
import { DEFAULT_OPERATIONS_VIEW, filterOperations } from './views/operationsViews';
import type { OperationsViewCriteria } from './views/operationsViews';

const REFRESH_INTERVAL_MS = 10_000;


export function App() {
  const [section, setSection] = useState<Section>(() => sectionFromHash());
  const [systemInfo, setSystemInfo] = useState<SystemInfoResponse | null>(null);
  const [currentUser, setCurrentUser] = useState<CurrentUserResponse | null>(null);
  const activeRefresh = useRef<AbortController | null>(null);
  const accessKey = useRef<string | null>(null);
  // A 401 means the browser sent no Windows credentials; polling would only repeat prompts.
  const signInRequired = useRef(false);
  const [executions, setExecutions] = useState<ExecutionResponse[]>([]);
  const [assetTypes, setAssetTypes] = useState<AssetTypeResponse[]>([]);
  const [assets, setAssets] = useState<AssetResponse[]>([]);
  const [assetCapabilities, setAssetCapabilities] = useState<AssetCreationCapabilitiesResponse | null>(null);
  const [logicModules, setLogicModules] = useState<LogicModuleResponse[]>([]);
  const [configuredLogic, setConfiguredLogic] = useState<ConfiguredLogicResponse[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [lastUpdated, setLastUpdated] = useState<Date | null>(null);

  useEffect(() => {
    const onHashChange = () => setSection(sectionFromHash());
    window.addEventListener('hashchange', onHashChange);
    return () => window.removeEventListener('hashchange', onHashChange);
  }, []);

  const refresh = useCallback(async () => {
    // Only the latest refresh may update the view.
    activeRefresh.current?.abort();
    const controller = new AbortController();
    activeRefresh.current = controller;
    setIsRefreshing(true);

    const clearWorkspace = () => {
      setSystemInfo(null); setExecutions([]); setAssetTypes([]); setAssets([]);
      setAssetCapabilities(null); setLogicModules([]); setConfiguredLogic([]); setLastUpdated(null);
    };
    try {
      const user = await loadSession(controller.signal);
      if (controller.signal.aborted) return;
      signInRequired.current = false;
      const nextKey = sessionAccessKey(user);
      if (nextKey !== accessKey.current) clearWorkspace();
      accessKey.current = nextKey;
      setCurrentUser(user);
      const snapshot = await loadWorkspaceSnapshot(user, controller.signal);
      if (controller.signal.aborted) return;
      const effectiveUser = { ...user, permissions: user.permissions.filter(p => !snapshot.deniedPermissions.includes(p)) };
      accessKey.current = sessionAccessKey(effectiveUser);
      setCurrentUser(effectiveUser);
      setSystemInfo(snapshot.system); setExecutions(snapshot.executions);
      setAssetTypes(snapshot.assetTypes); setAssets(snapshot.assets);
      setAssetCapabilities(snapshot.capabilities); setLogicModules(snapshot.modules);
      setConfiguredLogic(snapshot.configurations);
      setError(snapshot.deniedPermissions.length ? t('refresh.accessChanged')
        : snapshot.incomplete ? t('refresh.partial') : null);
      setLastUpdated(new Date());
    } catch (reason) {
      if (controller.signal.aborted ||
          (reason instanceof DOMException && reason.name === 'AbortError')) return;

      clearWorkspace(); setCurrentUser(null); accessKey.current = null;
      signInRequired.current = isSignInRequired(reason);
      setError(t(signInRequired.current ? 'refresh.signInRequired' : 'refresh.sessionUnavailable'));
    } finally {
      if (activeRefresh.current === controller) {
        activeRefresh.current = null;
        setIsRefreshing(false);
      }
    }
  }, []);

  useEffect(() => {
    void refresh();
    const interval = window.setInterval(() => {
      if (!signInRequired.current) void refresh();
    }, REFRESH_INTERVAL_MS);
    return () => {
      window.clearInterval(interval);
      activeRefresh.current?.abort();
      activeRefresh.current = null;
    };
  }, [refresh]);

  return (
    <div className="app-shell">
      <aside className="navigation">
        <div className="product-mark">
          <span className="product-kicker">Weatherford</span>
          <strong>WellSite AutoPilot</strong>
        </div>
        <nav aria-label="Primary">
          {canOpenSection(currentUser, 'operations') && <NavigationLink section="operations" current={section}>{t('nav.operations')}</NavigationLink>}
          {canOpenSection(currentUser, 'recommendations') && <NavigationLink section="recommendations" current={section}>{t('nav.recommendations')}</NavigationLink>}
          {canOpenSection(currentUser, 'assets') && <NavigationLink section="assets" current={section}>{t('nav.assets')}</NavigationLink>}
          {canOpenSection(currentUser, 'logic') && <NavigationLink section="logic" current={section}>{t('nav.logic')}</NavigationLink>}
        </nav>
        <UserMenu user={currentUser} isLoading={isRefreshing} />
      </aside>

      <main className="content">
        <PageHeader
          section={section}
          isRefreshing={section === 'recommendations' || section === 'access' ? false : isRefreshing}
          lastUpdated={section === 'recommendations' || section === 'access' ? null : lastUpdated}
          onRefresh={section === 'recommendations' || section === 'access' ? undefined : () => void refresh()}
        />

        {error && (
          <div className="status-error" role="alert">
            {error}
          </div>
        )}

        {isAwaitingAccess(currentUser) && currentUser && <AwaitingAccessNotice user={currentUser} />}
        {currentUser && !isAwaitingAccess(currentUser) && section !== 'access' && !canOpenSection(currentUser, section) && (
          <div className="status-error" role="alert">{t('session.sectionDenied')}</div>
        )}
        {section === 'operations' && canOpenSection(currentUser, section) && (
          <OperationsPage
            key={JSON.stringify([currentUser?.userId, currentUser?.permissions])}
            systemInfo={systemInfo}
            executions={executions}
            userId={currentUser?.userId}
          />
        )}
        {section === 'recommendations' && canOpenSection(currentUser, section) && (
          <RecommendationsPage
            key={JSON.stringify([currentUser?.userId, currentUser?.permissions])}
            userId={currentUser?.userId}
            permissions={currentUser?.permissions}
          />
        )}
        {section === 'access' && (
          currentUser?.userId && canOpenSection(currentUser, 'access')
            ? <AdministrationWorkspace key={currentUser.userId} currentUserId={currentUser.userId} />
            : <div className="status-error" role="alert">{t('access.denied')}</div>
        )}
        {section === 'assets' && canOpenSection(currentUser, section) && (
          <AssetsPage
            key={JSON.stringify([currentUser?.userId, currentUser?.permissions])}
            assetTypes={assetTypes}
            assets={assets}
            userId={currentUser?.userId}
            capabilities={assetCapabilities}
            onRefresh={refresh}
          />
        )}
        {section === 'logic' && canOpenSection(currentUser, section) && (
          <LogicPage
            key={JSON.stringify([currentUser?.userId, currentUser?.permissions])}
            userId={currentUser?.userId}
            assets={assets}
            assetTypes={assetTypes}
            onRefresh={refresh}
            modules={logicModules}
            configuredLogic={configuredLogic}
            permissions={currentUser?.permissions ?? []}
          />
        )}
      </main>
    </div>
  );
}

function PageHeader({
  section,
  isRefreshing,
  lastUpdated,
  onRefresh,
}: {
  section: Section;
  isRefreshing: boolean;
  lastUpdated: Date | null;
  onRefresh?: () => void;
}) {
  const copy = {
    operations: {
      eyebrow: 'Operations',
      title: 'Runtime activity',
      description: 'Recent Logic execution and runtime readiness on this Runtime Node.',
    },
    recommendations: {
      eyebrow: t('nav.recommendations'),
      title: t('inbox.pageTitle'),
      description: t('inbox.pageDescription'),
    },
    assets: {
      eyebrow: 'Assets',
      title: 'Asset inventory',
      description: 'Provider-neutral Assets and Asset Types available to configured Logic.',
    },
    logic: {
      eyebrow: 'Logic',
      title: 'Logic workspace',
      description: 'Installed Logic Modules and customer-specific configured Logic revisions.',
    },
    access: {
      eyebrow: t('access.eyebrow'),
      title: t('access.pageTitle'),
      description: t('access.pageDescription'),
    },
  }[section];

  return (
    <header className="page-header">
      <div>
        <p className="eyebrow">{copy.eyebrow}</p>
        <h1>{copy.title}</h1>
        <p className="page-description">{copy.description}</p>
      </div>
      <div className="header-actions">
        {lastUpdated && (
          <span className="last-updated">
            Updated {lastUpdated.toLocaleTimeString()}
          </span>
        )}
        {onRefresh && (
          <button
            className="secondary-button"
            type="button"
            disabled={isRefreshing}
            onClick={onRefresh}
          >
            {isRefreshing ? 'Refreshing…' : 'Refresh'}
          </button>
        )}
      </div>
    </header>
  );
}

function OperationsPage({
  systemInfo,
  executions,
  userId,
}: {
  systemInfo: SystemInfoResponse | null;
  executions: ExecutionResponse[];
  userId: string | null | undefined;
}) {
  const [viewCriteria, setViewCriteria] = useState<OperationsViewCriteria>(DEFAULT_OPERATIONS_VIEW);
  const [selectedExecutionId, setSelectedExecutionId] = useState<string | null>(null);
  const selectedIdStillAuthorized = selectedExecutionId !== null &&
    executions.some(execution => execution.executionId === selectedExecutionId);
  // Forget a selection as soon as it disappears from the authorized list.
  useEffect(() => {
    if (selectedExecutionId && !selectedIdStillAuthorized) {
      setSelectedExecutionId(null);
    }
  }, [selectedExecutionId, selectedIdStillAuthorized]);
  const visibleExecutions = useMemo(
    () => filterOperations(executions, viewCriteria),
    [executions, viewCriteria],
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
              {visibleExecutions.map(execution => (
                <tr key={execution.executionId}>
                  <td>
                    <StatusPill value={execution.status} />
                  </td>
                  <td>
                    <strong>{execution.assetExternalId}</strong>
                    <small>{execution.quantity}</small>
                  </td>
                  <td>
                    <strong>{execution.moduleId}</strong>
                    <small>v{execution.moduleVersion}</small>
                  </td>
                  <td>{execution.mode}</td>
                  <td>{formatTimestamp(execution.requestedAtUtc)}</td>
                  <td>
                    {execution.failureCode ??
                      execution.resultCode ??
                      (execution.status === 'Requested'
                        ? 'Queued for runtime'
                        : 'In progress')}
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
              ))}
              {visibleExecutions.length === 0 && (
                <EmptyRow columns={7}>{executions.length === 0
                  ? t('views.noActivity')
                  : t('views.noMatches')}</EmptyRow>
              )}
            </tbody>
          </table>
        </div>
        {selectedIdStillAuthorized && selectedExecutionId && (
          <ExecutionDetailPanel
            key={selectedExecutionId}
            executionId={selectedExecutionId}
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

function AssetsPage({
  assetTypes,
  assets,
  userId,
  capabilities,
  onRefresh,
}: {
  assetTypes: AssetTypeResponse[];
  assets: AssetResponse[];
  userId: string | null | undefined;
  capabilities: AssetCreationCapabilitiesResponse | null;
  onRefresh: () => Promise<void>;
}) {
  const [selected, setSelected] = useState<AssetResponse | null>(null);
  const selectedAuthorized = assets.find(asset => asset.id === selected?.id);
  useEffect(() => {
    if (selected && !assets.some(asset => asset.id === selected.id)) setSelected(null);
  }, [assets, selected]);
  const typeNames = useMemo(
    () => new Map(assetTypes.map(type => [type.id, type.displayName])),
    [assetTypes],
  );
  const [inventoryCriteria, setInventoryCriteria] = useState<AssetInventoryCriteria>(
    DEFAULT_ASSET_INVENTORY_CRITERIA,
  );
  const visibleAssets = useMemo(
    () => filterAssetInventory(assets, typeNames, inventoryCriteria),
    [assets, typeNames, inventoryCriteria],
  );
  const rootAssets = assets.filter(asset => !asset.parentAssetId).length;

  return (
    <>
      <section className="summary-grid" aria-label="Asset summary">
        <SummaryCard label="Assets" value={String(assets.length)} detail="Available to configured Logic" />
        <SummaryCard label="Asset Types" value={String(assetTypes.length)} detail="Provider-neutral definitions" />
        <SummaryCard label="Root Assets" value={String(rootAssets)} detail="Top-level hierarchy entries" />
      </section>

      <AssetCreationPanel
        key={userId ?? 'anonymous'}
        userId={userId}
        assetTypes={assetTypes}
        assets={assets}
        capabilities={capabilities}
        onCreated={async created => {
          setSelected(created);
          await onRefresh();
        }}
        onRefresh={onRefresh}
      />
      <section className="panel">
        <PanelHeader eyebrow="Inventory" title="Assets" count={visibleAssets.length} />
        <AssetInventoryControls
          userId={userId}
          criteria={inventoryCriteria}
          onChange={setInventoryCriteria}
          matchingCount={visibleAssets.length}
          totalCount={assets.length}
        />
        <div className="table-wrap">
          <table className="activity-table">
            <thead>
              <tr>
                <th>Name</th>
                <th>Type</th>
                <th>Hierarchy</th>
                <th>Status</th>
                <th>Created</th>
              </tr>
            </thead>
            <tbody>
              {visibleAssets.map(asset => (
                <tr key={asset.id}>
                  <td><button type="button" className="asset-select"
                    onClick={() => setSelected(asset)}
                    aria-label={t('assets.selectNamed', { name: asset.name ?? t('assets.unnamed') })}>
                    {asset.name}
                  </button></td>
                  <td>{typeNames.get(asset.assetTypeId) ?? asset.assetTypeId}</td>
                  <td>{asset.parentAssetId ? 'Child asset' : 'Root asset'}</td>
                  <td><StatusPill value={asset.isActive ? 'Active' : 'Inactive'} /></td>
                  <td>{formatTimestamp(asset.createdAtUtc)}</td>
                </tr>
              ))}
              {visibleAssets.length === 0 && (
                <EmptyRow columns={5}>
                  {assets.length === 0 ? t('assets.none') : t('assets.noMatches')}
                </EmptyRow>
              )}
            </tbody>
          </table>
        </div>
        {selectedAuthorized && <div className="asset-selected" aria-live="polite">
          <h3>{t('assets.selected')}</h3>
          <strong>{selectedAuthorized.name}</strong>
          <p>{t('assets.selectedType', { name:
            typeNames.get(selectedAuthorized.assetTypeId) ?? selectedAuthorized.assetTypeId ?? t('assets.unknownType') })}</p>
          <code>{selectedAuthorized.id}</code>
          <button type="button" className="secondary-button"
            onClick={() => setSelected(null)}>{t('assets.clearSelection')}</button>
        </div>}
      </section>

      <section className="panel">
        <PanelHeader eyebrow="Schema" title="Asset Types" count={assetTypes.length} />
        <div className="table-wrap">
          <table className="activity-table">
            <thead>
              <tr>
                <th>Display name</th>
                <th>Key</th>
                <th>Schema version</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {assetTypes.map(type => (
                <tr key={type.id}>
                  <td><strong>{type.displayName}</strong></td>
                  <td>{type.key}</td>
                  <td>{type.schemaVersion}</td>
                  <td><StatusPill value={type.isActive ? 'Active' : 'Inactive'} /></td>
                </tr>
              ))}
              {assetTypes.length === 0 && <EmptyRow columns={4}>No Asset Types are configured.</EmptyRow>}
            </tbody>
          </table>
        </div>
      </section>
    </>
  );
}

function LogicPage({
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
  const [selectedConfiguredId, setSelectedConfiguredId] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const canManage = mayManage && canReadConfigurations;
  const canCreate = canManage && assets.some(asset => asset.isActive);
  useEffect(() => { if (!canCreate) setCreating(false); }, [canCreate]);
  const selectedConfiguration = configuredLogic.find(item => item.id === selectedConfiguredId);
  useEffect(() => {
    if (selectedConfiguredId && !configuredLogic.some(item => item.id === selectedConfiguredId))
      setSelectedConfiguredId(null);
  }, [configuredLogic, selectedConfiguredId]);
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

function SummaryCard({
  label,
  value,
  detail,
}: {
  label: string;
  value: string;
  detail: string;
}) {
  return (
    <article className="summary-card">
      <span>{label}</span>
      <strong>{value}</strong>
      <small>{detail}</small>
    </article>
  );
}

function PanelHeader({
  eyebrow,
  title,
  count,
}: {
  eyebrow: string;
  title: string;
  count?: number;
}) {
  return (
    <div className="panel-header">
      <div>
        <p className="eyebrow">{eyebrow}</p>
        <h2>{title}</h2>
      </div>
      {count !== undefined && <span className="record-count">{count} shown</span>}
    </div>
  );
}

function StatusPill({ value }: { value?: string | null }) {
  return (
    <span className={`status-pill status-${toCssToken(value)}`}>
      {value ?? 'Unknown'}
    </span>
  );
}

function EmptyRow({
  columns,
  children,
}: {
  columns: number;
  children: string;
}) {
  return (
    <tr>
      <td className="empty-state" colSpan={columns}>{children}</td>
    </tr>
  );
}

function sectionFromHash(): Section {
  const value = window.location.hash.replace('#', '').toLowerCase();
  return value === 'assets' || value === 'logic' || value === 'recommendations' || value === 'access'
    ? value : 'operations';
}

function formatTimestamp(value?: string | null) {
  if (!value) {
    return '—';
  }

  const timestamp = new Date(value);
  return Number.isNaN(timestamp.getTime()) ? value : timestamp.toLocaleString();
}

function shortId(value?: string | null) {
  if (!value) {
    return '—';
  }

  return value.length > 12 ? `${value.slice(0, 8)}…` : value;
}

function toCssToken(value?: string | null) {
  return (value ?? 'unknown')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-');
}
