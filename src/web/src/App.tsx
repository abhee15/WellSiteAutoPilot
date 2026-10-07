import { useCallback, useEffect, useMemo, useState } from 'react';
import type {
  AssetResponse,
  AssetTypeResponse,
  ConfiguredLogicResponse,
  ExecutionResponse,
  LogicModuleResponse,
  SystemInfoResponse,
} from './api/generated/v1/types.gen';

const REFRESH_INTERVAL_MS = 10_000;
type Section = 'operations' | 'assets' | 'logic';

export function App() {
  const [section, setSection] = useState<Section>(() => sectionFromHash());
  const [systemInfo, setSystemInfo] = useState<SystemInfoResponse | null>(null);
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

  const refresh = useCallback(async (signal?: AbortSignal) => {
    setIsRefreshing(true);

    try {
      const responses = await Promise.all([
        fetch('/api/v1/system/info', { signal }),
        fetch('/api/v1/executions?limit=50', { signal }),
        fetch('/api/v1/asset-types', { signal }),
        fetch('/api/v1/assets?limit=100', { signal }),
        fetch('/api/v1/logic-modules?limit=100', { signal }),
        fetch('/api/v1/configured-logic?limit=100', { signal }),
      ]);

      const failed = responses.find(response => !response.ok);
      if (failed) {
        throw new Error(`Runtime data request failed (${failed.status}).`);
      }

      const [
        system,
        recentExecutions,
        availableAssetTypes,
        availableAssets,
        availableModules,
        configurations,
      ] = await Promise.all([
        responses[0].json() as Promise<SystemInfoResponse>,
        responses[1].json() as Promise<ExecutionResponse[]>,
        responses[2].json() as Promise<AssetTypeResponse[]>,
        responses[3].json() as Promise<AssetResponse[]>,
        responses[4].json() as Promise<LogicModuleResponse[]>,
        responses[5].json() as Promise<ConfiguredLogicResponse[]>,
      ]);

      setSystemInfo(system);
      setExecutions(recentExecutions);
      setAssetTypes(availableAssetTypes);
      setAssets(availableAssets);
      setLogicModules(availableModules);
      setConfiguredLogic(configurations);
      setError(null);
      setLastUpdated(new Date());
    } catch (reason) {
      if (reason instanceof DOMException && reason.name === 'AbortError') {
        return;
      }

      setError(
        reason instanceof Error
          ? reason.message
          : 'WellSite AutoPilot information is unavailable.',
      );
    } finally {
      setIsRefreshing(false);
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    void refresh(controller.signal);

    const interval = window.setInterval(() => {
      void refresh();
    }, REFRESH_INTERVAL_MS);

    return () => {
      controller.abort();
      window.clearInterval(interval);
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
          <NavigationLink section="operations" current={section}>Operations</NavigationLink>
          <NavigationLink section="assets" current={section}>Assets</NavigationLink>
          <NavigationLink section="logic" current={section}>Logic</NavigationLink>
        </nav>
        <button className="user-menu" type="button">User</button>
      </aside>

      <main className="content">
        <PageHeader
          section={section}
          isRefreshing={isRefreshing}
          lastUpdated={lastUpdated}
          onRefresh={() => void refresh()}
        />

        {error && (
          <div className="status-error" role="alert">
            {error}
          </div>
        )}

        {section === 'operations' && (
          <OperationsPage systemInfo={systemInfo} executions={executions} />
        )}
        {section === 'assets' && (
          <AssetsPage assetTypes={assetTypes} assets={assets} />
        )}
        {section === 'logic' && (
          <LogicPage modules={logicModules} configuredLogic={configuredLogic} />
        )}
      </main>
    </div>
  );
}

function NavigationLink({
  section,
  current,
  children,
}: {
  section: Section;
  current: Section;
  children: string;
}) {
  return (
    <a className={section === current ? 'active' : undefined} href={`#${section}`}>
      {children}
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
  onRefresh: () => void;
}) {
  const copy = {
    operations: {
      eyebrow: 'Operations',
      title: 'Runtime activity',
      description: 'Recent Logic execution and runtime readiness on this Runtime Node.',
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
        <button
          className="secondary-button"
          type="button"
          disabled={isRefreshing}
          onClick={onRefresh}
        >
          {isRefreshing ? 'Refreshing…' : 'Refresh'}
        </button>
      </div>
    </header>
  );
}

function OperationsPage({
  systemInfo,
  executions,
}: {
  systemInfo: SystemInfoResponse | null;
  executions: ExecutionResponse[];
}) {
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
        <PanelHeader eyebrow="Activity" title="Recent executions" count={executions.length} />
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
              </tr>
            </thead>
            <tbody>
              {executions.map(execution => (
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
                </tr>
              ))}
              {executions.length === 0 && (
                <EmptyRow columns={6}>No execution activity has been recorded.</EmptyRow>
              )}
            </tbody>
          </table>
        </div>
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
  const rootAssets = assets.filter(asset => !asset.parentAssetId).length;

  return (
    <>
      <section className="summary-grid" aria-label="Asset summary">
        <SummaryCard label="Assets" value={String(assets.length)} detail="Available to configured Logic" />
        <SummaryCard label="Asset Types" value={String(assetTypes.length)} detail="Provider-neutral definitions" />
        <SummaryCard label="Root Assets" value={String(rootAssets)} detail="Top-level hierarchy entries" />
      </section>

      <section className="panel">
        <PanelHeader eyebrow="Inventory" title="Assets" count={assets.length} />
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
              {assets.map(asset => (
                <tr key={asset.id}>
                  <td><strong>{asset.name}</strong></td>
                  <td>{typeNames.get(asset.assetTypeId) ?? asset.assetTypeId}</td>
                  <td>{asset.parentAssetId ? 'Child asset' : 'Root asset'}</td>
                  <td><StatusPill value={asset.isActive ? 'Active' : 'Inactive'} /></td>
                  <td>{formatTimestamp(asset.createdAtUtc)}</td>
                </tr>
              ))}
              {assets.length === 0 && <EmptyRow columns={5}>No Assets are configured.</EmptyRow>}
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
        <PanelHeader eyebrow="Catalog" title="Logic Modules" count={modules.length} />
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
              {modules.map(module => (
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
              {modules.length === 0 && <EmptyRow columns={6}>No Logic Modules are installed.</EmptyRow>}
            </tbody>
          </table>
        </div>
      </section>

      <section className="panel">
        <PanelHeader eyebrow="Configuration" title="Configured Logic" count={configuredLogic.length} />
        <div className="table-wrap">
          <table className="activity-table">
            <thead>
              <tr>
                <th>Name</th>
                <th>Active revision</th>
                <th>Revisions</th>
                <th>Latest status</th>
                <th>Created</th>
              </tr>
            </thead>
            <tbody>
              {configuredLogic.map(item => {
                const latest = [...(item.revisions ?? [])]
                  .sort((left, right) => (right.revisionNumber ?? 0) - (left.revisionNumber ?? 0))[0];

                return (
                  <tr key={item.id}>
                    <td><strong>{item.name}</strong></td>
                    <td>{item.activeRevisionId ? shortId(item.activeRevisionId) : '—'}</td>
                    <td>{item.revisions?.length ?? 0}</td>
                    <td><StatusPill value={latest?.status ?? 'Draft'} /></td>
                    <td>{formatTimestamp(item.createdAtUtc)}</td>
                  </tr>
                );
              })}
              {configuredLogic.length === 0 && <EmptyRow columns={5}>No Logic has been configured.</EmptyRow>}
            </tbody>
          </table>
        </div>
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
  return value === 'assets' || value === 'logic' ? value : 'operations';
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
