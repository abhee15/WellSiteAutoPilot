import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import {
  listGuidedModuleDrafts,
  listGuidedModuleProviders,
  listLogicModules,
  listPendingGuidedModuleSubmissions,
} from '../../src/api/generated/v1/sdk.gen';
import { AuthoringModules } from '../../src/views/AuthoringModules';
import { moduleStage } from '../../src/views/moduleStage';

vi.mock('../../src/api/generated/v1/sdk.gen', () => ({
  listGuidedModuleDrafts: vi.fn(),
  listGuidedModuleProviders: vi.fn(),
  listLogicModules: vi.fn(),
  listPendingGuidedModuleSubmissions: vi.fn(),
}));

const draft = (id: string, name: string, provider: string, extra: object = {}) => ({
  id,
  revision: 1,
  manifestJson: JSON.stringify({ moduleId: 'authoring.' + id, displayName: name, requiredAdapterKey: provider }),
  rulesJson: '{}',
  submitted: false,
  updatedAtUtc: '2026-11-26T12:00:00Z',
  ...extra,
});

afterEach(cleanup);

beforeEach(() => {
  vi.mocked(listGuidedModuleProviders).mockResolvedValue({
    data: [
      { adapterKey: 'foresite', displayName: 'ForeSite' },
      { adapterKey: 'simulator', displayName: 'Simulator' },
    ],
  } as never);
  vi.mocked(listLogicModules).mockResolvedValue({ data: [{ moduleId: 'authoring.d3' }] } as never);
  vi.mocked(listPendingGuidedModuleSubmissions).mockResolvedValue({ data: [{ draftId: 'd2' }] } as never);
});

test('shows each module at its stage and keeps empty modules out of the default view', async () => {
  vi.mocked(listGuidedModuleDrafts).mockResolvedValue({
    data: [
      draft('d1', 'SPM optimizer', 'foresite'),
      draft('d2', 'Choke step-down', 'simulator', { submitted: true }),
      draft('d3', 'Max-rate cap', 'foresite', { submitted: true }),
      draft('d4', 'Empty shell', 'simulator', { rulesJson: null }),
    ],
  } as never);
  render(<AuthoringModules />);
  const link = await screen.findByRole('link', { name: 'SPM optimizer' });
  expect(link.getAttribute('href')).toBe('#authoring/draft%3Ad1');
  expect(screen.queryByRole('link', { name: 'Empty shell' })).toBeNull();
  expect(await screen.findByRole('tab', { name: 'In review (1)' })).toBeTruthy();
  expect(screen.getByRole('tab', { name: 'Approved (1)' })).toBeTruthy();

  fireEvent.click(screen.getByRole('tab', { name: 'Approved (1)' }));
  expect(screen.getByRole('link', { name: 'Max-rate cap' })).toBeTruthy();
  expect(screen.queryByRole('link', { name: 'SPM optimizer' })).toBeNull();

  fireEvent.click(screen.getByRole('tab', { name: 'Not started (1)' }));
  expect(screen.getByRole('link', { name: 'Empty shell' })).toBeTruthy();
});

test('the provider select filters the list and starts a module for that provider', async () => {
  vi.mocked(listGuidedModuleDrafts).mockResolvedValue({
    data: [draft('d1', 'SPM optimizer', 'foresite'), draft('d5', 'Pump trim', 'simulator')],
  } as never);
  render(<AuthoringModules />);
  fireEvent.change(await screen.findByRole('combobox', { name: 'Provider' }), { target: { value: 'foresite' } });
  expect(screen.getByRole('link', { name: 'SPM optimizer' })).toBeTruthy();
  expect(screen.queryByRole('link', { name: 'Pump trim' })).toBeNull();
  expect(screen.getByRole('link', { name: 'New ForeSite module' }).getAttribute('href')).toBe(
    '#authoring/new%3Aforesite',
  );
});

test('explains how to start when there are no modules', async () => {
  vi.mocked(listGuidedModuleDrafts).mockResolvedValue({ data: [] } as never);
  render(<AuthoringModules />);
  expect(await screen.findByText('Create your first module')).toBeTruthy();
  expect(screen.getByRole('link', { name: 'New module' })).toBeTruthy();
});

test('a stage is never guessed when the other lists could not be read', () => {
  expect(moduleStage(draft('d3', 'x', 'foresite'), 'authoring.d3', null, null)).toBe('inProgress');
  expect(moduleStage(draft('d6', 'x', 'foresite', { rulesJson: null }), undefined, null, null)).toBe('notStarted');
  expect(moduleStage(draft('d3', 'x', 'foresite'), 'authoring.d3', new Set(['authoring.d3']), null)).toBe('approved');
});

const page = (index: number) => draft('p' + index, 'Module ' + index, 'foresite');

test('reads every page so the counts cover all modules, and retries a failed load in place', async () => {
  vi.mocked(listLogicModules).mockResolvedValue({ data: [] } as never);
  vi.mocked(listPendingGuidedModuleSubmissions).mockResolvedValue({ data: [] } as never);
  vi.mocked(listGuidedModuleDrafts)
    .mockResolvedValueOnce({ data: Array.from({ length: 25 }, (_, i) => page(i)) } as never)
    .mockResolvedValueOnce({ data: undefined } as never)
    .mockResolvedValueOnce({ data: [page(25)] } as never);
  render(<AuthoringModules />);
  fireEvent.click(await screen.findByRole('button', { name: 'Try again' }));
  expect(await screen.findByRole('tab', { name: 'All (26)' })).toBeTruthy();
  expect(screen.getByRole('link', { name: 'Module 25' })).toBeTruthy();
});
