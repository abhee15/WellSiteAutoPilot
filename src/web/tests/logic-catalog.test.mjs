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
  assert.match(html, /Logic module catalog controls/);
  assert.match(html, /role="group"/);
  assert.match(html, /<option value="DotNet">\.NET<\/option>/);
  assert.match(html, /Showing 2 of 3 loaded versions/);
});

test('the toolbar is one search box plus a Filters button, with the other filters in a closed panel', () => {
  const html = renderToStaticMarkup(createElement(controls.LogicCatalogControls, {
    criteria, onChange: () => {}, runtimes: [], matchingCount: 3, totalCount: 3,
  }));
  assert.equal(html.match(/type="search"/g)?.length, 1);
  assert.match(html, /aria-expanded="false"/);
  assert.match(html, /class="filter-toolbar-panel[^"]*" hidden=""/);
  assert.match(html, /Showing 3 of 3 loaded versions/);
});

test('groups stable module identities, orders numeric versions and does not alter authorization or filters', () => {
  const versions = [
    { id: 'old', moduleId: 'a', displayName: 'Pump', version: '1.9.0', isEnabled: true },
    { id: 'new', moduleId: 'a', displayName: 'Pump', version: '1.10.0', isEnabled: false },
    { id: 'other', moduleId: 'b', displayName: 'Pump', version: '2.0.0', isEnabled: true },
  ];
  assert.deepEqual(catalog.groupModuleVersions(versions).map(group => [group.key, ids(group.versions)]), [['a', ['new', 'old']], ['b', ['other']]]);
  assert.deepEqual(ids(catalog.groupModuleVersions(catalog.filterLogicCatalog(versions, { ...criteria, eligibility: 'enabled' }))[0].versions), ['old']);
  assert.equal(versions[0].id, 'old');
});

test('SemVer precedence preserves releases, prereleases and large numeric identifiers', () => {
  const ordered = ['1.0.0-alpha', '1.0.0-alpha.2', '1.0.0-alpha.10', '1.0.0-beta', '1.0.0', '1.0.1', '2.0.0'];
  for (let index = 1; index < ordered.length; index++) assert.ok(catalog.compareModuleVersions(ordered[index], ordered[index - 1]) > 0);
  assert.equal(catalog.compareModuleVersions('1.0.0+build.1', '1.0.0+build.2'), 0);
  assert.ok(catalog.compareModuleVersions('99999999999999999999.0.0', '9999999999999999999.0.0') > 0);
});
