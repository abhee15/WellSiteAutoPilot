import assert from 'node:assert/strict';
import { before, after, test } from 'node:test';
import { createServer } from 'vite';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

let vite, DataTable;
before(async () => {
  vite = await createServer({ server: { middlewareMode: true }, appType: 'custom', logLevel: 'silent' });
  ({ DataTable } = await vite.ssrLoadModule('/src/components/DataTable.tsx'));
});
after(async () => { await vite?.close(); });

const html = props => renderToStaticMarkup(createElement(DataTable, props,
  createElement('tbody', null, createElement('tr', null, createElement('td', null, 'cell')))));

test('a table is wrapped for horizontal scrolling and carries its accessible name', () => {
  assert.equal(html({ label: 'Users' }),
    '<div class="table-wrap"><table class="activity-table" aria-label="Users"><tbody><tr><td>cell</td></tr></tbody></table></div>');
});

test('page hooks are added after the base class, never instead of it', () => {
  assert.match(html({ label: 'Recommendations', className: 'recommendation-proposals' }),
    /<table class="activity-table recommendation-proposals" aria-label="Recommendations">/);
});

test('an embedded table gets the compact look rather than the panel gutters', () => {
  const markup = html({ label: 'Differences for Well 1', variant: 'embedded' });
  assert.match(markup, /<table class="data-table-embedded" aria-label="Differences for Well 1">/);
  assert.doesNotMatch(markup, /activity-table/);
});
