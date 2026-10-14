import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type {
  AssetResponse,
  AssetTypeResponse,
  ConfiguredLogicResponse,
  CurrentUserResponse,
  ExecutionResponse,
  LogicModuleResponse,
  SystemInfoResponse,
} from './api/generated/v1/types.gen';
import { t } from './localization';
import { OperationsViewControls } from './views/OperationsViewControls';
import { ExecutionDetailPanel } from './views/ExecutionDetailPanel';
import { RecommendationsPage } from './views/RecommendationsPage';
import { AssetInventoryControls } from './views/AssetInventoryControls';
import { LogicCatalogControls } from './views/LogicCatalogControls';
import { ConfiguredLogicControls } from './views/ConfiguredLogicControls';
import { ConfiguredLogicRevisionOverview } from './views/ConfiguredLogicRevisionOverview';
import { DEFAULT_CONFIGURED_LOGIC_CRITERIA, filterConfiguredLogic, latestRevision } from './views/configuredLogicViews';
import type { ConfiguredLogicCriteria } from './views/configuredLogicViews';
import { DEFAULT_LOGIC_CATALOG_CRITERIA, filterLogicCatalog, availableLogicRuntimes } from './views/logicCatalog';
import type { LogicCatalogCriteria } from './views/logicCatalog';
import { DEFAULT_ASSET_INVENTORY_CRITERIA, filterAssetInventory } from './views/assetInventory';
import type { AssetInventoryCriteria } from './views/assetInventory';
import { DEFAULT_OPERATIONS_VIEW, filterOperations } from './views/operationsViews';
import type { OperationsViewCriteria } from './views/operationsViews';

const REFRESH_INTERVAL_MS = 10_000;
type Section = 'operations' | 'recommendations' | 'assets' | 'logic';

export function App() {
  const [section, setSection] = useState<Section>(() => sectionFromHash());
  const [systemInfo, setSystemInfo] = useState<SystemInfoResponse | null>(null);
  const [currentUser, setCurrentUser] = useState<CurrentUserResponse | null>(null);
  const activeRefresh = useRef<AbortController | null>(null);
  const [executions, setExecutions] = useState<ExecutionResponse[]>([]);
  const [assetTypes, setAssetTypes] = useState<AssetTypeResponse[]>([]);
  const [assets, setAssets] = useState<AssetResponse[]>([]);
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

    try {
      const responses = await Promise.all([
        fetch('/api/v1/system/info', { signal: controller.signal }),
        fetch('/api/v1/executions?limit=50', { signal: controller.signal }),
        fetch('/api/v1/asset-types', { signal: controller.signal }),
        fetch('/api/v1/assets?limit=100', { signal: controller.signal }),
        fetch('/api/v1/logic-modules?limit=100', { signal: controller.signal }),
        fetch('/api/v1/configured-logic?limit=100', { signal: controller.signal }),
        fetch('/api/v1/security/me', { signal: controller.signal }),
      ]);

      if (controller.signal.aborted) return;
      const failed = responses.find(response => !response.ok);
      if (failed) {
        if (failed.status === 401 || failed.status === 403) {
          // Remove previously authorized information when the session loses access.
          setCurrentUser(null);
          setSystemInfo(null);
          setExecutions([]);
          setAssetTypes([]);
          setAssets([]);
          setLogicModules([]);
          setConfiguredLogic([]);
          setLastUpdated(null);
        }
        throw new Error(
          failed.status === 401 || failed.status === 403
            ? t('refresh.sessionUnavailable')
            : t('refresh.runtimeFailed', { status: failed.status }),
        );
      }

      const [system, recentExecutions, availableAssetTypes, availableAssets,
        availableModules, configurations, user] = await Promise.all([
        responses[0].json() as Promise<SystemInfoResponse>,
        responses[1].json() as Promise<ExecutionResponse[]>,
        responses[2].json() as Promise<AssetTypeResponse[]>,
        responses[3].json() as Promise<AssetResponse[]>,
        responses[4].json() as Promise<LogicModuleResponse[]>,
        responses[5].json() as Promise<ConfiguredLogicResponse[]>,
        responses[6].json() as Promise<CurrentUserResponse>,
      ]);
      if (controller.signal.aborted) return;

      setSystemInfo(system);
      setExecutions(recentExecutions);
      setAssetTypes(availableAssetTypes);
      setAssets(availableAssets);
      setLogicModules(availableModules);
      setConfiguredLogic(configurations);
      setCurrentUser(user);
      setError(null);
      setLastUpdated(new Date());
    } catch (reason) {
      if (controller.signal.aborted ||
          (reason instanceof DOMException && reason.name === 'AbortError')) return;

      setError(reason instanceof Error
        ? reason.message
        : t('refresh.unavailable'));
    } finally {
      if (activeRefresh.current === controller) {
        activeRefresh.current = null;
        setIsRefreshing(false);
      }
    }
  }, []);

  useEffect(() => {
    void refresh();
    const interval = window.setInterval(() => void refresh(), REFRESH_INTERVAL_MS);
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
          <NavigationLink section="operations" current={section}>{t('nav.operations')}</NavigationLink>
          <NavigationLink section="recommendations" current={section}>{t('nav.recommendations')}</NavigationLink>
          <NavigationLink section="assets" current={section}>{t('nav.assets')}</NavigationLink>
          <NavigationLink section="logic" current={section}>{t('nav.logic')}</NavigationLink>
        </nav>
        <UserMenu user={currentUser} isLoading={isRefreshing} />
      </aside>

      <main className="content">
        <PageHeader
          section={section}
          isRefreshing={section === 'recommendations' ? false : isRefreshing}
          lastUpdated={section === 'recommendations' ? null : lastUpdated}
          onRefresh={section === 'recommendations' ? undefined : () => void refresh()}
        />

        {error && (
          <div className="status-error" role="alert">
            {error}
          </div>
        )}

        {section === 'operations' && (
          <OperationsPage
            key={currentUser?.userId ?? 'no-user'}
            systemInfo={systemInfo}
            executions={executions}
            userId={currentUser?.userId}
          />
        )}
        {section === 'recommendations' && (
          <RecommendationsPage
            key={currentUser?.userId ?? 'no-user'}
            userId={currentUser?.userId}
          />
        )}
        {section === 'assets' && (
          <AssetsPage
            key={currentUser?.userId ?? 'no-user'}
            assetTypes={assetTypes}
            assets={assets}
          />
        )}
        {section === 'logic' && (
          <LogicPage
            key={currentUser?.userId ?? 'no-user'}
            modules={logicModules}
            configuredLogic={configuredLogic}
          />
        )}
      </main>
    </div>
  );
}

export function UserMenu({
  user,
  isLoading,
}: {
  user: CurrentUserResponse | null;
  isLoading: boolean;
}) {
  const displayName = user?.displayName?.trim() || user?.identityName ||
    (isLoading ? t('session.loading') : t('session.unavailable'));

  return (
    <details className="user-menu">
      <summary aria-label={t('session.label', { name: displayName })}>
        <span className="user-avatar" aria-hidden="true">
          {displayName.charAt(0).toUpperCase()}
        </span>
        <span className="user-label">{displayName}</span>
        <span className="user-menu-chevron" aria-hidden="true">⌃</span>
      </summary>
      <div className="user-panel">
        <strong>{t('session.heading')}</strong>
        {!user ? (
          <p role="status">{t('session.noDetails')}</p>
        ) : (
          <dl>
            <dt>{t('session.identity')}</dt>
            <dd>{user.identityName}</dd>
            <dt>{t('session.account')}</dt>
            <dd>{user.isActive ? t('session.active') : t('session.inactive')}</dd>
            <dt>{t('session.roles')}</dt>
            <dd>{user.roles?.length ? user.roles.join(', ') : t('session.noRoles')}</dd>
            <dt>{t('session.scope')}</dt>
            <dd>{t('session.assigned', { count: user.assetScopeIds?.length ?? 0 })}</dd>
          </dl>
        )}
        <p className="user-panel-note">{t('session.authNotice')}</p>
      </div>
    </details>
  );
}

export function NavigationLink({
  section,
  current,
  children,
}: {
  section: Section;
  current: Section;
  children: string;
}) {
  const shortLabel = { operations: 'OP', recommendations: 'RC', assets: 'AS', logic: 'LG' }[section];

  return (
    <a
      aria-current={current === section ? 'page' : undefined}
      aria-label={children}
      className={section === current ? 'active' : undefined}
      href={'#' + section}
    >
      <span className="nav-label">{children}</span>
      <span className="nav-short" aria-hidden="true">{shortLabel}</span>
    </a>
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
}: {
  assetTypes: AssetTypeResponse[];
  assets: AssetResponse[];
}) {
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

      <section className="panel">
        <PanelHeader eyebrow="Inventory" title="Assets" count={visibleAssets.length} />
        <AssetInventoryControls
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
                  <td><strong>{asset.name}</strong></td>
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
}: {
  modules: LogicModuleResponse[];
  configuredLogic: ConfiguredLogicResponse[];
}) {
  const [catalogCriteria, setCatalogCriteria] = useState<LogicCatalogCriteria>(
    DEFAULT_LOGIC_CATALOG_CRITERIA,
  );
  const [configuredCriteria, setConfiguredCriteria] = useState<ConfiguredLogicCriteria>(
    DEFAULT_CONFIGURED_LOGIC_CRITERIA,
  );
  const [selectedConfiguredId, setSelectedConfiguredId] = useState<string | null>(null);
  const selectedConfiguration = configuredLogic.find(item => item.id === selectedConfiguredId);
  useEffect(() => {
    if (selectedConfiguredId && !selectedConfiguration) setSelectedConfiguredId(null);
  }, [selectedConfiguredId, selectedConfiguration]);
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
        <SummaryCard label="Installed modules" value={String(modules.length)} detail="Immutable module versions" />
        <SummaryCard label="Trusted and enabled" value={String(trusted)} detail="Eligible for configuration" />
        <SummaryCard label="Active configurations" value={String(activeConfigurations)} detail="Configured Logic with active revision" />
      </section>

      <section className="panel">
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
      </section>

      <section className="panel">
        <PanelHeader eyebrow="Configuration" title="Configured Logic"
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
                        onClick={() => setSelectedConfiguredId(current =>
                          current === item.id ? null : item.id ?? null)}
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
        {selectedConfiguration && (
          <ConfiguredLogicRevisionOverview
            key={selectedConfiguration.id}
            logic={selectedConfiguration}
            onClose={() => setSelectedConfiguredId(null)}
          />
        )}
      </section>
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
  return value === 'assets' || value === 'logic' || value === 'recommendations'
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
