import { useRef } from 'react';
import { createRoot } from 'react-dom/client';
import { OverviewPage } from '../../src/views/OverviewPage';
import { RecommendationsPage } from '../../src/views/RecommendationsPage';
import { useSplitPaneViewport } from '../../src/components/useSplitPaneViewport';
import '@fontsource/roboto/latin-400.css';
import '@fontsource/roboto/latin-500.css';
import '../../src/tokens.css';
import '../../src/styles.css';

// Synthetic, authorized-looking data. No provider or production endpoint is contacted.
const id = (n: number) => `88888888-8888-4888-8888-${String(n).padStart(12, '0')}`;
const assets = [{ id: id(1), name: 'North Well', isActive: true }];
const permissions = ['assets.read', 'executions.read', 'configured-logic.read', 'recommendations.read', 'recommendations.decide', 'connections.read'];
const configuredLogic = [{ id: id(2), name: 'Pump watch', revisions: [] }];
const runs = Array.from({ length: 5 }, (_, n) => ({
  executionId: id(10 + n), assetId: id(1), logicInstanceId: id(2), moduleId: 'pump.watch',
  mode: 'Recommendation', status: 'Completed', requestedAtUtc: new Date(Date.now() - n * 60_000).toISOString(),
}));
const proposals = Array.from({ length: 6 }, (_, n) => ({
  id: id(30 + n), executionId: runs[Math.floor(n / 2)].executionId, assetId: id(1), configuredLogicId: id(2),
  moduleId: 'pump.watch', status: 'Created', canDecide: true, command: `Set pump speed ${n + 1}`,
  suggestedValue: 48 + n, unit: 'Hz', quantity: 'PumpSpeed',
  createdAtUtc: new Date(Date.now() - n * 60_000).toISOString(), expiresAtUtc: new Date(Date.now() + 3600_000).toISOString(),
}));
const actions = [50, 51].map(n => ({
  id: id(n), recommendationId: id(n - 20), executionId: id(10), assetId: id(1),
  command: `Set frequency ${n}`, value: 48, unit: 'Hz', status: 'Ambiguous',
  gateCode: 'SINK_AMBIGUOUS', canManage: false, createdAtUtc: new Date(Date.now() - 120_000).toISOString(),
}));
const response = (value: unknown) => new Response(JSON.stringify(value), { status: 200 });
window.fetch = async resource => {
  const url = new URL(String(resource), window.location.origin);
  if (url.pathname === '/api/v1/recommendations') {
    const runId = url.searchParams.get('executionId');
    return response(runId ? proposals.filter(p => p.executionId === runId) : proposals);
  }
  if (url.pathname.startsWith('/api/v1/recommendations/')) return response(proposals.find(p => p.id === url.pathname.split('/').pop()));
  if (url.pathname === '/api/v1/control/actions') return response(actions);
  if (url.pathname === '/api/v1/control/kill-switch') return response({ engaged: false, autonomousControlEnabled: true });
  if (url.pathname === '/api/v1/assets/owners') return response([]);
  return response([]);
};
const overview = new URLSearchParams(window.location.search).get('page') === 'overview';
function App() {
  const main = useRef<HTMLElement>(null);
  useSplitPaneViewport(main);
  return <div className="app-shell nav-collapsed">
    <aside className="navigation" aria-label="Primary navigation"><div className="product-mark"><strong>WS</strong></div></aside>
    <main className="content" ref={main}>
      <header className="page-header"><h1>{overview ? 'Overview' : 'Recommendations'}</h1></header>
      <div className="page-body">{overview
        ? <OverviewPage permissions={permissions} assets={assets} configuredLogic={configuredLogic} executions={runs} systemHealth={{ unhealthyCount: 0 }} isLoaded userId={id(99)} />
        : <RecommendationsPage permissions={permissions} assets={assets} configuredLogic={configuredLogic} userId={id(99)} />}
      </div>
    </main>
  </div>;
}
createRoot(document.getElementById('root')!).render(<App />);
