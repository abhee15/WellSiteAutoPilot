import { useCallback, useEffect, useState } from 'react';
import type {
  ExecutionResponse,
  SystemInfoResponse,
} from './api/generated/v1/types.gen';

const REFRESH_INTERVAL_MS = 10_000;

export function App() {
  const [systemInfo, setSystemInfo] = useState<SystemInfoResponse | null>(null);
  const [executions, setExecutions] = useState<ExecutionResponse[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [lastUpdated, setLastUpdated] = useState<Date | null>(null);

  const refresh = useCallback(async (signal?: AbortSignal) => {
    setIsRefreshing(true);

    try {
      const [systemResponse, executionResponse] = await Promise.all([
        fetch('/api/v1/system/info', { signal }),
        fetch('/api/v1/executions?limit=50', { signal }),
      ]);

      if (!systemResponse.ok) {
        throw new Error(`System information request failed (${systemResponse.status}).`);
      }

      if (!executionResponse.ok) {
        throw new Error(`Execution activity request failed (${executionResponse.status}).`);
      }

      const [system, recentExecutions] = await Promise.all([
        systemResponse.json() as Promise<SystemInfoResponse>,
        executionResponse.json() as Promise<ExecutionResponse[]>,
      ]);

      setSystemInfo(system);
      setExecutions(recentExecutions);
      setError(null);
      setLastUpdated(new Date());
    } catch (reason) {
      if (reason instanceof DOMException && reason.name === 'AbortError') {
        return;
      }

      setError(
        reason instanceof Error
          ? reason.message
          : 'Operations information is unavailable.',
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
    <div className="app-shell">
      <aside className="navigation">
        <div className="product-mark">
          <span className="product-kicker">Weatherford</span>
          <strong>WellSite AutoPilot</strong>
        </div>
        <nav aria-label="Primary">
          <a className="active" href="#operations">Operations</a>
          <a href="#assets">Assets</a>
          <a href="#logic">Logic</a>
        </nav>
        <button className="user-menu" type="button">User</button>
      </aside>

      <main className="content" id="operations">
        <header className="page-header">
          <div>
            <p className="eyebrow">Operations</p>
            <h1>Runtime activity</h1>
            <p className="page-description">
              Recent Logic execution and runtime readiness on this Runtime Node.
            </p>
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
              onClick={() => void refresh()}
            >
              {isRefreshing ? 'Refreshing…' : 'Refresh'}
            </button>
          </div>
        </header>

        {error && (
          <div className="status-error" role="alert">
            {error}
          </div>
        )}

        <section className="summary-grid" aria-label="Operations summary">
          <article className="summary-card">
            <span>Runtime health</span>
            <strong>
              {components.length === 0
                ? 'Unknown'
                : unhealthyComponents === 0
                  ? 'Healthy'
                  : 'Attention'}
            </strong>
            <small>
              {components.length === 0
                ? 'Waiting for runtime status'
                : `${components.length - unhealthyComponents}/${components.length} components healthy`}
            </small>
          </article>
          <article className="summary-card">
            <span>Active executions</span>
            <strong>{activeExecutions}</strong>
            <small>Requested through waiting</small>
          </article>
          <article className="summary-card">
            <span>Requires attention</span>
            <strong>{attentionExecutions}</strong>
            <small>Failed, suspended, or timed out</small>
          </article>
        </section>

        <section className="panel">
          <div className="panel-header">
            <div>
              <p className="eyebrow">Activity</p>
              <h2>Recent executions</h2>
            </div>
            <span className="record-count">{executions.length} shown</span>
          </div>

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
                      <span
                        className={`status-pill status-${toCssToken(execution.status)}`}
                      >
                        {execution.status ?? 'Unknown'}
                      </span>
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
                  <tr>
                    <td className="empty-state" colSpan={6}>
                      No execution activity has been recorded on this Runtime Node.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </section>

        <section className="panel compact-panel">
          <div className="panel-header">
            <div>
              <p className="eyebrow">Runtime node</p>
              <h2>Service health</h2>
            </div>
          </div>
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
            {!systemInfo && !error && (
              <div className="status-loading">Loading runtime health…</div>
            )}
          </div>
        </section>
      </main>
    </div>
  );
}

function formatTimestamp(value?: string | null) {
  if (!value) {
    return '—';
  }

  const timestamp = new Date(value);

  if (Number.isNaN(timestamp.getTime())) {
    return value;
  }

  return timestamp.toLocaleString();
}

function toCssToken(value?: string | null) {
  return (value ?? 'unknown')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-');
}
