import assert from 'node:assert/strict';
import { before, after, test } from 'node:test';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { createServer } from 'vite';

let vite;
let views;
let controls;

before(async () => {
  vite = await createServer({
    server: { middlewareMode: true },
    appType: 'custom',
    logLevel: 'silent',
  });
  views = await vite.ssrLoadModule('/src/views/operationsViews.ts');
  controls = await vite.ssrLoadModule('/src/views/OperationsViewControls.tsx');
});

after(async () => {
  await vite?.close();
});

const rows = [
  {
    executionId: 'exe-2', assetExternalId: 'WELL-102',
    moduleId: 'weatherford.fill', moduleVersion: '1.0.0',
    quantity: 'PumpFillage', status: 'Failed', mode: 'Shadow',
    resultCode: null, failureCode: 'PROVIDER_TIMEOUT',
    requestedAtUtc: '2026-10-14T10:00:00Z',
  },
  {
    executionId: 'exe-3', assetExternalId: 'WELL-101',
    moduleId: 'weatherford.pressure', moduleVersion: '1.0.0',
    quantity: 'Pressure', status: 'Completed', mode: 'Shadow',
    resultCode: 'SUCCESS', failureCode: null,
    requestedAtUtc: '2026-10-14T10:05:00Z',
  },
  {
    executionId: 'exe-1', assetExternalId: 'WELL-103',
    moduleId: 'weatherford.fill', moduleVersion: '1.0.0',
    quantity: 'PumpFillage', status: 'Running', mode: 'Shadow',
    resultCode: null, failureCode: null,
    requestedAtUtc: '2026-10-14T09:30:00Z',
  },
];

function storage() {
  const values = new Map();
  return {
    getItem(key) { return values.get(key) ?? null; },
    setItem(key, value) { values.set(key, value); },
    snapshot() { return [...values.entries()]; },
  };
}

test('search and status operate only on recent server-authorized rows', () => {
  assert.deepEqual(
    views.filterOperations(rows, {
      search: 'well-102',
      status: 'attention',
      sort: 'newest',
    }).map(row => row.executionId),
    ['exe-2'],
  );
  assert.deepEqual(
    views.filterOperations(rows, { search: '', status: 'active', sort: 'newest' })
      .map(row => row.executionId),
    ['exe-1'],
  );
  assert.deepEqual(
    views.filterOperations(rows, { search: '', status: 'completed', sort: 'newest' })
      .map(row => row.executionId),
    ['exe-3'],
  );
  assert.deepEqual(
    views.filterOperations(rows, {
      search: 'provider_timeout',
      status: 'all',
      sort: 'newest',
    }).map(row => row.executionId),
    ['exe-2'],
  );
  assert.equal(rows.length, 3, 'filtering must not mutate the server-provided list');
});

test('newest and oldest sorts are deterministic, without mutating input', () => {
  assert.deepEqual(
    views.filterOperations(rows, { search: '', status: 'all', sort: 'newest' })
      .map(row => row.executionId),
    ['exe-3', 'exe-2', 'exe-1'],
  );
  assert.deepEqual(
    views.filterOperations(rows, { search: '', status: 'all', sort: 'oldest' })
      .map(row => row.executionId),
    ['exe-1', 'exe-2', 'exe-3'],
  );
  assert.equal(rows[0].executionId, 'exe-2');
});

test('missing execution identifiers and timestamps still sort safely', () => {
  const incomplete = [
    { status: 'Running', requestedAtUtc: null, executionId: undefined },
    { status: 'Running', requestedAtUtc: null, executionId: 'exe-known' },
  ];
  assert.deepEqual(
    views.filterOperations(incomplete, { search: '', status: 'all', sort: 'newest' })
      .map(row => row.executionId),
    [undefined, 'exe-known'],
  );
});

test('saved views persist only preferences and are isolated by current user', () => {
  const memory = storage();
  const saved = [{
    id: 'view-1',
    name: 'Shift attention',
    criteria: { search: 'well', status: 'attention', sort: 'newest' },
  }];
  assert.equal(views.persistSavedOperationsViews('user-A', saved, memory), true);
  assert.deepEqual(views.loadSavedOperationsViews('user-A', memory), saved);
  assert.deepEqual(views.loadSavedOperationsViews('user-B', memory), []);
  assert.equal(memory.snapshot().length, 1);
  assert.equal(memory.snapshot()[0][1].includes('WELL-102'), false,
    'execution rows must not be persisted');
});

test('corrupt, oversized, invalid, and duplicate stored views fail closed', () => {
  assert.deepEqual(views.parseSavedOperationsViews('{bad json'), []);
  assert.deepEqual(views.parseSavedOperationsViews('{"version":2,"views":[]}'), []);
  assert.deepEqual(views.parseSavedOperationsViews('x'.repeat(60000)), []);

  const valid = { id: 'one', name: 'Safe view', criteria: views.DEFAULT_OPERATIONS_VIEW };
  const invalid = {
    id: 'two', name: 'Dangerous', criteria: {
      search: '', status: 'ignored', sort: 'newest',
    },
  };
  const duplicate = {
    id: 'three', name: 'SAFE VIEW', criteria: views.DEFAULT_OPERATIONS_VIEW,
  };
  assert.deepEqual(
    views.parseSavedOperationsViews(JSON.stringify({
      version: 1, views: [valid, invalid, duplicate],
    })),
    [valid],
  );
});

test('storage unavailability never claims the view was saved', () => {
  const broken = {
    getItem() { throw new Error('Storage disabled'); },
    setItem() { throw new Error('Quota exceeded'); },
  };
  assert.deepEqual(views.loadSavedOperationsViews('user-A', broken), []);
  assert.equal(views.persistSavedOperationsViews('user-A', [], broken), false);
  assert.equal(views.persistSavedOperationsViews(null, [], storage()), false);
  assert.equal(views.persistSavedOperationsViews(
    'user-A',
    Array.from({ length: views.MAX_SAVED_OPERATIONS_VIEWS + 1 }, (_, index) => ({
      id: String(index), name: 'View ' + index, criteria: views.DEFAULT_OPERATIONS_VIEW,
    })),
    storage(),
  ), false);
});

test('view controls keep filtering available without claiming session storage', () => {
  const markup = renderToStaticMarkup(createElement(controls.OperationsViewControls, {
    userId: null,
    criteria: views.DEFAULT_OPERATIONS_VIEW,
    onChange: () => {},
    matchingCount: 2,
    recentCount: 3,
  }));
  assert.match(markup, /Showing 2 of 3 recent executions/);
  assert.match(markup, /Save view/);
  assert.match(markup, /disabled=""/);
  assert.match(markup, /Execution view controls/);
});
