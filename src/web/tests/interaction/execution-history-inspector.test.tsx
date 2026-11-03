import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ExecutionHistory } from '../../src/views/ExecutionHistory';
import { OperationsPage } from '../../src/views/OperationsPage';
import { runHistoryHash } from '../../src/views/assetActivity';
import { accessibilityViolations } from './axe';

const id = (n: number) => `88888888-8888-4888-8888-${String(n).padStart(12, '0')}`;
const assetId = id(1);
const logicId = id(2);
const run = (n: number) => ({
  executionId: id(n), logicInstanceId: logicId, status: 'Completed', mode: 'Shadow',
  trigger: 'Manual', moduleId: 'sample.module', requestedAtUtc: `2026-11-01T12:${String(n).padStart(2, '0')}:00Z`,
});
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
const assets = [{ id: assetId, name: 'Well 1', assetTypeId: id(80), isActive: true }];
const configuredLogic = [{ id: logicId, name: 'Pump observer', revisions: [] }];
afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.location.hash = ''; });

function mockHistory(pages: Record<string, { items: ReturnType<typeof run>[]; nextCursor: string | null }>) {
  const http = vi.fn(async (url: string) => {
    if (url.startsWith('/api/v1/executions/history?')) {
      const params = new URL(url, 'http://test').searchParams;
      return response(pages[params.get('after') ?? 'first'] ?? { items: [], nextCursor: null });
    }
    if (url.startsWith('/api/v1/executions/')) return response(run(Number(url.split('-').at(-1))));
    return response([]);
  });
  vi.stubGlobal('fetch', http);
  return http;
}

it('shows a selected run beside the scoped history, retains selection and provides run navigation', async () => {
  const http = mockHistory({ first: { items: [run(11), run(10)], nextCursor: null } });
  const user = userEvent.setup();
  const { container } = render(<ExecutionHistory assetId={assetId} logicId={logicId}
    assets={assets as never} configuredLogic={configuredLogic as never} focused />);
  const buttons = await screen.findAllByRole('button', { name: /Inspect run/ });
  await waitFor(() => expect(container.querySelector('[data-selected="true"]')).toBeTruthy());
  expect(container.querySelector('.execution-history-focus .split-panes')).toBeTruthy();
  expect(container.querySelector('.execution-history-inspector-content')).toBeTruthy();
  expect(await screen.findByRole('heading', { name: 'Run details' })).toBeTruthy();
  expect((container.querySelector('[data-selected="true"]') as HTMLElement).textContent).toContain('11:00');
  expect(http.mock.calls.some(([url]) => {
    const params = new URL(url, 'http://test').searchParams;
    return params.get('assetId') === assetId && params.get('configuredLogicId') === logicId;
  })).toBe(true);

  await user.click(buttons[1]!);
  await waitFor(() => expect((container.querySelector('[data-selected="true"]') as HTMLElement).textContent).toContain('10:00'));
  expect(screen.getByRole('button', { name: 'Next run' })).toHaveProperty('disabled', true);
  await user.click(screen.getByRole('button', { name: 'Previous run' }));
  await waitFor(() => expect((container.querySelector('[data-selected="true"]') as HTMLElement).textContent).toContain('11:00'));
  expect(await accessibilityViolations(container)).toEqual([]);
});

it('keeps a direct older run visible even when it is absent from the loaded history page', async () => {
  mockHistory({ first: { items: [run(11), run(10)], nextCursor: null } });
  const onRunSelect = vi.fn();
  const { container } = render(<ExecutionHistory assetId={assetId} logicId={logicId} runId={id(99)}
    onRunSelect={onRunSelect} assets={assets as never} configuredLogic={configuredLogic as never} focused />);
  expect(await screen.findByRole('heading', { name: 'Run details' })).toBeTruthy();
  expect(screen.getByText('This run is outside the current history page.')).toBeTruthy();
  expect(container.querySelector('[data-selected="true"]')).toBeNull();
  expect(onRunSelect).not.toHaveBeenCalled();
});

it('preserves the selected authorized run across keyset paging rather than selecting a different row', async () => {
  mockHistory({
    first: { items: [run(11), run(10)], nextCursor: 'older-cursor' },
    'older-cursor': { items: [run(9)], nextCursor: null },
  });
  const user = userEvent.setup();
  const { container } = render(<ExecutionHistory assetId={assetId} logicId={logicId}
    assets={assets as never} configuredLogic={configuredLogic as never} focused />);
  await screen.findByRole('heading', { name: 'Run details' });
  await user.click(screen.getByRole('button', { name: 'Older runs' }));
  expect(await screen.findByText('This run is outside the current history page.')).toBeTruthy();
  expect(container.querySelector('[data-selected="true"]')).toBeNull();
  expect(screen.getByRole('heading', { name: 'Run details' })).toBeTruthy();
  await user.click(screen.getByRole('button', { name: 'Newest runs' }));
  await waitFor(() => expect(container.querySelector('[data-selected="true"]')).toBeTruthy());
});

it('changes the asset-history URL without remounting the history and keeps its selected row', async () => {
  window.location.hash = runHistoryHash(assetId, logicId);
  mockHistory({ first: { items: [run(11), run(10)], nextCursor: null } });
  const user = userEvent.setup();
  const { container } = render(<OperationsPage executions={[]} assets={assets as never}
    configuredLogic={configuredLogic as never} userId={id(90)} />);
  const buttons = await screen.findAllByRole('button', { name: /Inspect run/ });
  await waitFor(() => expect(window.location.hash).toContain('runId'));
  const history = container.querySelector('.execution-history');
  await user.click(buttons[1]!);
  await waitFor(() => expect(window.location.hash).toContain(encodeURIComponent(id(10))));
  expect(container.querySelector('.execution-history')).toBe(history);
  expect(within(container.querySelector('.execution-history-browse') as HTMLElement)
    .getAllByRole('button', { name: /Inspect run/ })).toHaveLength(2);
  expect((container.querySelector('[data-selected="true"]') as HTMLElement).textContent).toContain('10:00');
});
