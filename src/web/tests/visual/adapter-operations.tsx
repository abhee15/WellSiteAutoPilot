import { useRef } from 'react';
import { createRoot } from 'react-dom/client';
import { AdaptersPanel } from '../../src/views/AdaptersPanel';
import { useSplitPaneViewport } from '../../src/components/useSplitPaneViewport';
import '@fontsource/roboto/latin-400.css';
import '../../src/tokens.css';
import '../../src/styles.css';
import '../../src/views/splitLayout.css';

// Synthetic descriptor fixtures exercise the actual explorer; no provider or authenticated API is called.
const fields = Array.from({ length: 30 }, (_, index) => ({ name: `measurement${index}`, dataType: 'decimal|null', required: true,
  description: 'A source engineering value, preserving its original timestamp, unit and reliability.' }));
const operations = Array.from({ length: 40 }, (_, index) => ({ key: `current.${index}`, displayName: `Current measurement ${index + 1}`,
  description: 'A bounded read from the selected source.', capability: 'CurrentData', capabilityId: 'engineering.current', contractVersion: '1.0', effect: 'ReadOnly',
  request: { id: 'engineering.current-request/v1', fields }, response: { id: 'engineering.value/v1', fields },
  timestampSemantics: 'Original source timestamp in UTC.', qualitySemantics: 'Original source reliability.',
  limits: { maximumQuantities: 1, maximumValueBytes: 65536, maximumReplyBytes: 262144, timeoutSeconds: 6 } }));
const originalFetch = window.fetch.bind(window);
window.fetch = async (resource, init) => {
  const path = String(resource);
  if (path === '/api/v1/connections/adapter-operations') return new Response(JSON.stringify([
    { adapterKey: 'fixture', displayName: 'Synthetic adapter', description: 'Responsive Operations layout fixture.', operations },
  ]), { headers: { 'content-type': 'application/json' } });
  if (path === '/api/v1/connections') return new Response('[]', { headers: { 'content-type': 'application/json' } });
  return originalFetch(resource, init);
};
const params = new URLSearchParams(window.location.search);
const listOnly = params.get('mode') === 'list';
function App() {
  const main = useRef<HTMLElement>(null);
  useSplitPaneViewport(main);
  return <div className={params.get('nav') === 'expanded' ? 'app-shell' : 'app-shell nav-collapsed'}>
    <aside className="navigation" aria-label="Primary navigation"><div className="product-mark"><strong>WS</strong></div>
      <nav aria-label="Sections"><a href="#connections" aria-current="page">Connections</a></nav></aside>
    <main className="content" ref={main}><header className="page-header"><h1>Adapter operations</h1></header>
      <div className="page-body"><AdaptersPanel /></div></main>
  </div>;
}
createRoot(document.getElementById('root')!).render(<App />);
window.setInterval(() => {
  const layout = document.querySelector<HTMLElement>('.adapter-operations');
  const browse = document.querySelector<HTMLElement>('.split-browse');
  const inspector = document.querySelector<HTMLElement>('.split-selected');
  const rows = [...document.querySelectorAll<HTMLElement>('.split-results li')];
  if (!layout || !browse || !inspector || rows.length === 0) return;
  if (!listOnly && !inspector.querySelector('.split-type-detail')) { rows[0].querySelector('button')?.click(); return; }
  if (!listOnly && !inspector.querySelector('table')) {
    [...inspector.querySelectorAll('button')].find(button => button.textContent === 'Request and response')?.click(); return;
  }
  const visible = (element: HTMLElement | null) => !!element && getComputedStyle(element).display !== 'none' && element.getBoundingClientRect().width > 0 && element.getBoundingClientRect().height > 0;
  const rect = (element: HTMLElement) => {
    const box = element.getBoundingClientRect();
    return { left: box.left, right: box.right };
  };
  const selected = browse.querySelector<HTMLElement>('[data-selected="true"]');
  const other = browse.querySelector<HTMLElement>('[data-selected="false"]');
  document.getElementById('visual-report')!.textContent = 'VISUAL_REPORT:' + JSON.stringify({
    mode: listOnly ? 'list' : 'selected', viewport: window.innerWidth, viewportClient: document.documentElement.clientWidth,
    documentWidth: document.documentElement.scrollWidth, bodyWidth: document.body.scrollWidth,
    pageBodyClient: document.querySelector('.page-body')!.clientWidth, pageBodyScroll: document.querySelector('.page-body')!.scrollWidth,
    container: layout.getBoundingClientRect().width, browseVisible: visible(browse), inspectorVisible: visible(inspector),
    detailVisible: visible(inspector.querySelector('.split-type-detail')), selectedOperation: inspector.querySelector('h2')?.textContent,
    backVisible: visible(inspector.querySelector('.split-back')), selectedRowVisible: visible(selected),
    selectedRowColor: selected ? getComputedStyle(selected).backgroundColor : '', otherRowColor: other ? getComputedStyle(other).backgroundColor : '',
    detailOverflow: getComputedStyle(inspector).overflowY, browseOverflow: getComputedStyle(browse).overflowY,
    browseRect: rect(browse), inspectorRect: rect(inspector),
  }) + ':END';
}, 120);
