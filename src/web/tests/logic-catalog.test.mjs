import assert from 'node:assert/strict';
import { before, after, test } from 'node:test';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { createServer } from 'vite';

let vite, catalog, controls;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' });
  catalog = await vite.ssrLoadModule('/src/views/logicCatalog.ts');
  controls = await vite.ssrLoadModule('/src/views/LogicCatalogControls.tsx');
});
after(async () => { await vite?.close(); });

const modules = [
  { id: 'b', moduleId: 'wfd.pressure', displayName: 'Pressure', version: '2.0',
    publisher: 'Weatherford', runtime: 'DotNet', isEnabled: true,
    executionProfile: 'ScheduledOneShot', installedAtUtc: '2026-10-13T10:00:00Z' },
  { id: 'c', moduleId: 'custom.fill', displayName: 'Fillage', version: '1.0',
    publisher: 'Customer', runtime: 'Python', isEnabled: false,
    executionProfile: 'ScheduledOneShot', installedAtUtc: '2026-10-14T10:00:00Z' },
  { id: 'a', moduleId: 'wfd.fill', displayName: 'Fillage', version: '2.0',
    publisher: 'Weatherford', runtime: 'DotNet', isEnabled: true,
    executionProfile: 'ScheduledOneShot', installedAtUtc: '2026-10-12T10:00:00Z' },
];
const criteria = { search: '', eligibility: 'all', runtime: 'all', sort: 'name' };
const ids = values => values.map(module => module.id);

test('search matches only authorized records by module and publisher', () => {
  assert.deepEqual(ids(catalog.filterLogicCatalog(modules,
    { ...criteria, search: 'CUSTOM.fill' })), ['c']);
  assert.deepEqual(ids(catalog.filterLogicCatalog(modules,
    { ...criteria, search: 'Weatherford' })), ['a', 'b']);
  assert.deepEqual(catalog.filterLogicCatalog(modules,
    { ...criteria, search: 'missing module' }), []);
  assert.equal(modules.length, 3);
});
test('enabled state and runtime filters compose without changing input', () => {
  assert.deepEqual(ids(catalog.filterLogicCatalog(modules,
    { ...criteria, eligibility: 'enabled', runtime: 'DotNet' })), ['a', 'b']);
  assert.deepEqual(ids(catalog.filterLogicCatalog(modules,
    { ...criteria, eligibility: 'disabled', runtime: 'Python' })), ['c']);
  assert.deepEqual(catalog.filterLogicCatalog(modules,
    { ...criteria, eligibility: 'disabled', runtime: 'DotNet' }), []);
  assert.deepEqual(catalog.availableLogicRuntimes(modules), ['DotNet', 'Python']);
});
test('name ties and installation ordering are deterministic', () => {
  assert.deepEqual(ids(catalog.filterLogicCatalog(modules, criteria)), ['a', 'c', 'b']);
  assert.deepEqual(ids(catalog.filterLogicCatalog(modules,
    { ...criteria, sort: 'newest' })), ['c', 'b', 'a']);
  assert.deepEqual(ids(catalog.filterLogicCatalog(modules,
    { ...criteria, sort: 'oldest' })), ['a', 'b', 'c']);
  assert.equal(modules[0].id, 'b');
});
test('controls have accessible labels and dynamic runtime options', () => {
  const html = renderToStaticMarkup(createElement(controls.LogicCatalogControls, {
    criteria, onChange: () => {}, runtimes: ['DotNet', 'Python'],
    matchingCount: 2, totalCount: 3,
  }));
  assert.match(html, /Logic Module catalog controls/);
  assert.match(html, /role="group"/);
  assert.match(html, /<option value="DotNet">DotNet<\/option>/);
  assert.match(html, /Showing 2 of 3 loaded modules/);
});

test('the toolbar is one search box plus a Filters button, with the other filters in a closed panel', () => {
  const html = renderToStaticMarkup(createElement(controls.LogicCatalogControls, {
    criteria, onChange: () => {}, runtimes: [], matchingCount: 3, totalCount: 3,
  }));
  assert.equal(html.match(/type="search"/g)?.length, 1);
  assert.match(html, /aria-expanded="false"/);
  assert.match(html, /class="filter-toolbar-panel[^"]*" hidden=""/);
  assert.match(html, /Showing 3 of 3 loaded modules/);
});
