import { useEffect, useState } from 'react';
import type { SystemInfoResponse } from './api/generated/types.gen';

export function App() {
  const [systemInfo, setSystemInfo] = useState<SystemInfoResponse | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();

    fetch('/api/v1/system/info', { signal: controller.signal })
      .then(async response => {
        if (!response.ok) {
          throw new Error(`System information request failed (${response.status}).`);
        }

        return response.json() as Promise<SystemInfoResponse>;
      })
      .then(setSystemInfo)
      .catch(reason => {
        if (reason instanceof DOMException && reason.name === 'AbortError') {
          return;
        }

        setError(reason instanceof Error ? reason.message : 'System information is unavailable.');
      });

    return () => controller.abort();
  }, []);

  const components = systemInfo?.components ?? [];

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

      <main className="content">
        <header className="page-header">
          <div>
            <p className="eyebrow">System</p>
            <h1>System Health</h1>
          </div>
          <span className="environment-badge">Development</span>
        </header>

        <section className="status-panel" aria-live="polite">
          {error && <div className="status-error">{error}</div>}
          {!error && !systemInfo && <div className="status-loading">Loading system health…</div>}
          {systemInfo && (
            <div className="status-grid">
              {components.map(component => (
                <article className="status-card" key={component.name}>
                  <div>
                    <h2>{component.name}</h2>
                    <p>Version {component.version}</p>
                  </div>
                  <strong>{component.status}</strong>
                </article>
              ))}
            </div>
          )}
        </section>
      </main>
    </div>
  );
}
