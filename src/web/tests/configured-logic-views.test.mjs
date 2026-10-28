import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { createServer } from 'vite';

let vite, views, controls, history, summary;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true },
    appType: 'custom', logLevel: 'silent' });
  views = await vite.ssrLoadModule('/src/views/configuredLogicViews.ts');
  controls = await vite.ssrLoadModule('/src/views/ConfiguredLogicControls.tsx');
  history = await vite.ssrLoadModule('/src/views/ConfiguredLogicRevisionOverview.tsx');
  summary = await vite.ssrLoadModule('/src/views/logicSummary.ts');
});
after(async () => { await vite?.close(); });

const revisions = [
  { id: 'rev2', revisionNumber: 2, status: 'Draft', moduleId: 'wfd.fillage',
    moduleVersion: '1.1', parametersJson: 'SECRET_SHOULD_NOT_RENDER',
    schedule: { enabled: true, cadenceSeconds: 60,
      startAtUtc: '2026-10-14T08:00:00Z', timeZoneId: 'America/Chicago' },
    assetBindings: [{ assetId: 'asset-one' }], dataBindings: [] },
  { id: 'rev1', revisionNumber: 1, status: 'Active', moduleId: 'wfd.fillage',
    moduleVersion: '1.0' },
];
const rows = [
  { id: 'z', name: 'Pump Observer', activeRevisionId: 'rev1',
    createdAtUtc: '2026-10-12T12:00:00Z', revisions },
  { id: 'a', name: 'Well Calculation', activeRevisionId: null,
    createdAtUtc: '2026-10-14T12:00:00Z', revisions: [
      { id: 'wellrev', revisionNumber: 1, status: 'Validated', moduleId: 'wfd.pressure' },
    ] },
  { id: 'b', name: 'Pump Observer', activeRevisionId: null,
    createdAtUtc: '2026-10-13T12:00:00Z', revisions: [] },
];
const criteria = { search: '', lifecycle: 'all', revisionStatus: 'all', sort: 'name' };
const ids = values => values.map(row => row.id);

test('latest revision uses revision number, not active ID, and does not mutate input', () => {
  assert.equal(views.latestRevision(rows[0]).id, 'rev2');
  assert.deepEqual(views.orderedRevisions(rows[0]).map(item => item.id), ['rev2', 'rev1']);
  assert.equal(views.latestRevision(rows[2]), null);
  assert.equal(rows[0].revisions[0].id, 'rev2');
});

test('asset-scoped authorized inputs remain the entire search universe', () => {
  assert.deepEqual(ids(views.filterConfiguredLogic(rows,
    { ...criteria, search: 'PRESSURE' })), ['a']);
  assert.deepEqual(ids(views.filterConfiguredLogic(rows,
    { ...criteria, search: 'not-authorized' })), []);
  assert.deepEqual(ids(views.filterConfiguredLogic(rows,
    { ...criteria, search: 'PUMP' })), ['b', 'z']);
});

test('active configuration and latest revision status are independent filters', () => {
  assert.deepEqual(ids(views.filterConfiguredLogic(rows,
    { ...criteria, lifecycle: 'active', revisionStatus: 'draft' })), ['z']);
  assert.deepEqual(ids(views.filterConfiguredLogic(rows,
    { ...criteria, lifecycle: 'inactive', revisionStatus: 'validated' })), ['a']);
  assert.deepEqual(views.filterConfiguredLogic(rows,
    { ...criteria, lifecycle: 'active', revisionStatus: 'active' }), []);
});

test('name and chronological sort are deterministic with tie breakers', () => {
  assert.deepEqual(ids(views.filterConfiguredLogic(rows, criteria)), ['b', 'z', 'a']);
  assert.deepEqual(ids(views.filterConfiguredLogic(rows,
    { ...criteria, sort: 'newest' })), ['a', 'b', 'z']);
  assert.deepEqual(ids(views.filterConfiguredLogic(rows,
    { ...criteria, sort: 'oldest' })), ['z', 'b', 'a']);
  assert.equal(rows[0].id, 'z');
});

test('responsive filter controls provide labels and scoped result counts', () => {
  const html = renderToStaticMarkup(createElement(controls.ConfiguredLogicControls, {
    criteria, onChange: () => {}, matchingCount: 2, totalCount: 3,
  }));
  assert.match(html, /Configured Logic filters/);
  assert.match(html, /role="group"/);
  assert.match(html, /Latest revision status/);
  assert.match(html, /Showing 2 of 3 loaded configurations/);
});

test('logic overview leads with the name and key facts, and holds no secrets or revision table', () => {
  const html = renderToStaticMarkup(createElement(
    history.ConfiguredLogicRevisionOverview, {
      logic: rows[0],
      onClose: () => {},
    },
  ));
  assert.match(html, /role="region"/);
  assert.match(html, /<h3>[^<]+<\/h3>/);
  assert.match(html, /Live revision/);
  assert.match(html, /wfd\.fillage/);
  assert.match(html, /wfd\.fillage v1\.0/);
  assert.doesNotMatch(html, /<table/);
  assert.doesNotMatch(html, /SECRET_SHOULD_NOT_RENDER/);
  assert.doesNotMatch(html, /<textarea/);
});

test('a schedule reads as an interval and zone, or Off', () => {
  assert.equal(summary.scheduleText(revisions[0].schedule), 'Every 1 min · America/Chicago');
  assert.equal(summary.scheduleText({ enabled: false, cadenceSeconds: 60 }), 'Off');
  assert.equal(summary.scheduleText(undefined), 'Off');
});

test('empty revision histories render a stable informative placeholder', () => {
  const html = renderToStaticMarkup(createElement(
    history.ConfiguredLogicRevisionOverview, {
      logic: rows[2], onClose: () => {},
    },
  ));
  assert.match(html, /No revisions available/);
});
