import { useRef } from 'react';
import { createRoot } from 'react-dom/client';
import { ExecutionHistory } from '../../src/views/ExecutionHistory';
import { useSplitPaneViewport } from '../../src/components/useSplitPaneViewport';
import '@fontsource/roboto/latin-400.css';
import '@fontsource/roboto/latin-500.css';
import '@fontsource/roboto/latin-700.css';
import '../../src/tokens.css';
import '../../src/styles.css';

/**
 * Isolated visual fixture of the production run-history component, with authorized-looking
 * synthetic responses. No authentication, provider, or production endpoint is contacted.
 * Only served by Vite middleware in the dedicated browser test (not shipped with the app).
 */
const id = (n: number) => `88888888-8888-4888-8888-${String(n).padStart(12, '0')}`;
const assetId = id(1);
const logicId = id(2);
const runs = Array.from({ length: 25 }, (_, i) => {
  const n = 30 - i;
  return {
    executionId: id(n), logicInstanceId: logicId, configurationRevisionId: id(3),
    status: n % 7 === 0 ? 'Failed' : 'Completed', mode: 'Shadow', trigger: 'Manual',
    moduleId: 'pump.observer', moduleVersion: '1.0.0', requestContractVersion: 3,
    requestedAtUtc: `2026-11-01T12:${String(n).padStart(2, '0')}:00Z`,
    completedAtUtc: `2026-11-01T12:${String(n).padStart(2, '0')}:08Z`,
    outputJson: JSON.stringify({
      values: Array.from({ length: 35 }, (_, k) => ({
        point: `Well 101 / telemetry ${k}`, quality: 'Good',
        reading: k * 17.39, observedAtUtc: '2026-11-01T12:00:00Z',
      })),
    }),
  };
});
const assets = [{ id: assetId, name: 'RPOC_00002', assetTypeId: id(90), isActive: true }];
const configuredLogic = [{ id: logicId, name: 'Pressure observer', activeRevisionId: id(3), revisions: [{
  id: id(3), revisionNumber: 1, status: 'Active', mode: 'Shadow',
  assetBindings: [{ assetId }],
}] }];
const response = (value: unknown) => new Response(JSON.stringify(value), {
  status: 200, headers: { 'content-type': 'application/json' },
});
const originalFetch = window.fetch.bind(window);
window.fetch = async (resource: RequestInfo | URL, init?: RequestInit) => {
  const path = String(resource);
  if (path.startsWith('/api/v1/executions/history?')) {
    return response({ items: runs, nextCursor: null });
  }
  if (path.startsWith('/api/v1/executions/')) {
    const runId = path.slice('/api/v1/executions/'.length);
    const found = runs.find(run => run.executionId === runId);
    return found ? response(found) : new Response(null, { status: 404 });
  }
  return originalFetch(resource, init);
};

const listOnly = new URLSearchParams(window.location.search).get('mode') === 'list';
function App() {
  const main = useRef<HTMLElement>(null);
  useSplitPaneViewport(main);
  return <div className="app-shell nav-collapsed">
    <aside className="navigation" aria-label="Primary navigation">
      <div className="product-mark"><strong>WS</strong></div>
      <nav aria-label="Sections"><a href="#operations" aria-current="page">Runs</a></nav>
    </aside>
    <main className="content" ref={main}>
      <header className="page-header"><h1>Run history</h1></header>
      <div className="page-body">
        <section className="panel asset-page" aria-label="Run history">
          <header className="asset-page-header">
            <div><h3>Run history</h3><p>RPOC_00002 · Pressure observer</p>
              <p className="asset-page-meta">Runs for this asset and configuration</p>
            </div>
          </header>
          <ExecutionHistory focused assetId={assetId} logicId={logicId}
            runId={listOnly ? null : runs[0].executionId}
            onRunSelect={listOnly ? () => undefined : undefined}
            assets={assets as never} configuredLogic={configuredLogic as never} />
        </section>
      </div>
    </main>
  </div>;
}
createRoot(document.getElementById('root')!).render(<App />);

function inspectLayout() {
  const layout = document.querySelector<HTMLElement>('.execution-history-focus');
  const browse = document.querySelector<HTMLElement>('.execution-history-browse');
  const inspector = document.querySelector<HTMLElement>('.execution-history-inspector');
  const detail = document.querySelector<HTMLElement>('.execution-history-inspector-content');
  const selectedRow = document.querySelector<HTMLElement>('.execution-history-row[data-selected="true"]');
  const otherRow = document.querySelector<HTMLElement>('.execution-history-row:not([data-selected="true"])');
  const back = document.querySelector<HTMLElement>('.execution-history-inspector .split-back');
  const report = document.getElementById('visual-report');
  if (!layout || !browse || !inspector || !report) return;
  if (!browse.querySelector('.execution-history-row')) return;
  if (!listOnly && !inspector.querySelector('.execution-detail')) return;
  const visible = (element: HTMLElement | null) => Boolean(element && getComputedStyle(element).display !== 'none' &&
    element.getBoundingClientRect().height > 0 && element.getBoundingClientRect().width > 0);
  const rect = (element: HTMLElement | null) => {
    if (!element) return null;
    const box = element.getBoundingClientRect();
    return { left: Math.round(box.left), top: Math.round(box.top), width: Math.round(box.width),
      right: Math.round(box.right), height: Math.round(box.height) };
  };
  const data = {
    mode: listOnly ? 'list' : 'selected',
    viewport: window.innerWidth,
    container: Math.round(layout.getBoundingClientRect().width),
    documentWidth: document.documentElement.scrollWidth,
    browseVisible: visible(browse),
    inspectorVisible: visible(inspector),
    detailVisible: visible(detail),
    backVisible: visible(back),
    selectedRowVisible: visible(selectedRow),
    selectedRowColor: selectedRow ? getComputedStyle(selectedRow).backgroundColor : '',
    otherRowColor: otherRow ? getComputedStyle(otherRow).backgroundColor : '',
    browseOverflow: getComputedStyle(browse).overflowY,
    inspectorOverflow: getComputedStyle(inspector).overflowY,
    detailOverflow: detail ? getComputedStyle(detail).overflowY : '',
    browseRect: rect(browse), inspectorRect: rect(inspector),
    selectedRun: document.querySelector('.execution-history-selected-header h4')?.textContent ?? '',
  };
  report.textContent = 'VISUAL_REPORT:' + JSON.stringify(data) + ':END';
}
window.setInterval(inspectLayout, 120);
