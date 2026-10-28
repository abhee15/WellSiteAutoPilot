import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { OperationsPage } from '../../src/views/OperationsPage';

afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.location.hash = ''; });

const executionId = '55555555-5555-4555-8555-555555555555';
const run = { executionId, status: 'Succeeded', mode: 'Shadow', trigger: 'Manual', moduleId: 'sample.module',
  moduleVersion: '1.0.0', requestContractVersion: 3, requestedAtUtc: '2026-10-28T03:00:00Z' };

it('opens a run beside the list when its row is clicked, marks the row, and closes back to the full list', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify(run))));
  const user = userEvent.setup();
  const { container } = render(<OperationsPage executions={[run] as never} userId="u1" />);
  const layout = container.querySelector('.runs-layout');
  expect(layout?.getAttribute('data-has-selection')).toBe('false');

  await user.click(within(container.querySelector('tbody')!).getByText('sample.module'));
  expect(await screen.findByRole('heading', { name: 'Run details' })).toBeTruthy();
  expect(layout?.getAttribute('data-has-selection')).toBe('true');
  expect(container.querySelector('tbody tr')?.getAttribute('data-selected')).toBe('true');

  await user.click(screen.getByRole('button', { name: 'Back to runs' }));
  expect(layout?.getAttribute('data-has-selection')).toBe('false');
});
