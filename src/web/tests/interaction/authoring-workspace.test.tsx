import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AuthoringWorkbench } from '../../src/views/AuthoringWorkbench';
import { moveBindings, renameInExpression, updateNode } from '../../src/views/authoringDraft';
import * as api from '../../src/views/authoring';
import * as connections from '../../src/views/connections';
import type { Analysis, ModuleDraft, ModuleRun } from '../../src/views/authoring';

vi.mock('../../src/views/authoring', async (original) => ({
  ...(await original<typeof import('../../src/views/authoring')>()),
  loadStarters: vi.fn(),
  validateDraft: vi.fn(),
  evaluateDraft: vi.fn(),
}));
vi.mock('../../src/views/connections', async (original) => ({
  ...(await original<typeof import('../../src/views/connections')>()),
  loadConnections: vi.fn(),
  discoverConnection: vi.fn(),
  listPoints: vi.fn(),
}));
afterEach(() => {
  cleanup();
  vi.resetAllMocks();
});

const draft: ModuleDraft = {
  name: 'Pressure status',
  description: null,
  inputs: [{ name: 'Casing', quantity: 'CasingPressure', unit: 'psia', basis: 'Current', required: true }],
  parameters: [{ name: 'Limit', value: '250' }],
  steps: [{ name: 'IsHigh', expression: 'Casing > Limit' }],
  outputs: [{ name: 'High_out', kind: 'value', expression: 'IsHigh' }],
};
const analysis = (fingerprint: string): Analysis => ({
  valid: true,
  problems: [],
  graph: [
    { name: 'Casing', kind: 'input', dependsOn: [], usedBy: ['IsHigh'] },
    { name: 'Limit', kind: 'parameter', dependsOn: [], usedBy: ['IsHigh'] },
    { name: 'IsHigh', kind: 'step', dependsOn: ['Casing', 'Limit'], usedBy: ['High_out'] },
    { name: 'High_out', kind: 'output', dependsOn: ['IsHigh'], usedBy: [] },
  ],
  order: ['IsHigh'],
  fingerprint,
});
const run: ModuleRun = {
  outcome: 'Runs',
  reasons: [],
  fingerprint: 'one',
  problems: [],
  inputs: [
    {
      name: 'Casing',
      resolution: 'Sample',
      value: { kind: 'Number', number: 300, flag: null, text: null, reason: null },
      unit: 'psia',
      source: 'sample',
      basis: null,
      usedInput: null,
      reasons: [],
      candidates: [],
      required: true,
    },
  ],
  parameters: [
    {
      name: 'Limit',
      value: { kind: 'Number', number: 250, flag: null, text: null, reason: null },
      expression: null,
      dependsOn: [],
    },
  ],
  steps: [
    {
      name: 'IsHigh',
      value: { kind: 'Flag', number: null, flag: true, text: null, reason: null },
      expression: 'Casing > Limit',
      dependsOn: ['Casing', 'Limit'],
    },
  ],
  outputs: [
    {
      name: 'High_out',
      kind: 'value',
      value: { kind: 'Flag', number: null, flag: true, text: null, reason: null },
      unit: null,
      target: null,
      expression: 'IsHigh',
      plan: null,
      note: null,
    },
  ],
};

it('renames a node and the calculations that use it, without touching text or function names', () => {
  expect(renameInExpression('Casing > Limit && "Casing"', 'Casing', 'Csg')).toBe('Csg > Limit && "Casing"');
  expect(renameInExpression('max(max, 1)', 'max', 'peak')).toBe('max(peak, 1)');
  const renamed = updateNode(draft, 'input', 'Casing', { name: 'Csg' });
  expect(renamed.inputs[0].name).toBe('Csg');
  expect(renamed.steps[0].expression).toBe('Csg > Limit');
  expect(updateNode(draft, 'input', 'Casing', { name: '1bad' }).steps[0].expression).toBe('Casing > Limit');
});

it('moves bindings with a renamed input and drops them with a removed one', () => {
  const bindings = { inputs: { Casing: [{ input: 'surveillance:CasingPressure', basis: 'Current' }] } };
  expect(moveBindings(bindings, 'Casing', 'Csg').inputs).toEqual({ Csg: bindings.inputs.Casing });
  expect(moveBindings(bindings, 'Casing', null).inputs).toEqual({});
  expect(moveBindings(bindings, 'Other', 'X')).toBe(bindings);
});

it('tests a module against typed values and marks the evidence stale after an edit', async () => {
  vi.mocked(api.loadStarters).mockResolvedValue([{ id: 'threshold', title: 'Pressure status', summary: '', draft }]);
  vi.mocked(connections.loadConnections).mockResolvedValue([]);
  vi.mocked(api.validateDraft).mockImplementation(async (next) =>
    analysis(next.steps[0].expression === 'Casing > Limit' ? 'one' : 'two'),
  );
  vi.mocked(api.evaluateDraft).mockResolvedValue(run);
  render(<AuthoringWorkbench />);
  const user = userEvent.setup();

  await user.click(await screen.findByRole('button', { name: /Pressure status|Gated/ }));
  await screen.findByText('Valid');
  fireEvent.change(screen.getByLabelText('Value for Casing'), { target: { value: '300' } });
  await user.click(screen.getByRole('button', { name: 'Run test' }));
  await screen.findByText('Runs on this asset. Every required input resolved.');
  expect(vi.mocked(api.evaluateDraft).mock.calls[0][1]).toEqual({
    samples: { Casing: '300' },
    parameters: {},
    bindings: {},
  });
  expect(screen.queryByText(/results are out of date/)).toBeNull();

  await user.click(screen.getAllByRole('button', { name: /IsHigh/ })[0]);
  fireEvent.change(screen.getByLabelText('Expression'), { target: { value: 'Casing >= Limit' } });
  await screen.findByText(/results are out of date/);
  await waitFor(() =>
    expect(
      vi.mocked(api.validateDraft).mock.calls.some((call) => call[0].steps[0].expression === 'Casing >= Limit'),
    ).toBe(true),
  );
});

it('edits a rule table row and a history window, and shows which rule decided', async () => {
  const gated: ModuleDraft = {
    ...draft,
    inputs: [{ ...draft.inputs[0], history: { daysBack: 0, atLocal: 'now', windowMinutes: 240, reduce: 'avg' } }],
    steps: [
      {
        name: 'IsHigh',
        expression: 'false',
        rules: [{ when: 'Casing > Limit', then: 'true', reason: 'Above the limit' }],
      },
    ],
  };
  vi.mocked(api.loadStarters).mockResolvedValue([{ id: 'gated', title: 'Gated', summary: '', draft: gated }]);
  vi.mocked(connections.loadConnections).mockResolvedValue([]);
  vi.mocked(api.validateDraft).mockResolvedValue(analysis('one'));
  vi.mocked(api.evaluateDraft).mockResolvedValue({
    ...run,
    steps: [{ ...run.steps[0], matched: 'Rule 1: Above the limit' }],
  });
  render(<AuthoringWorkbench />);
  const user = userEvent.setup();

  await user.click(await screen.findByRole('button', { name: /Pressure status|Gated/ }));
  await screen.findByText('Valid');
  await user.click(screen.getAllByRole('button', { name: /Casing/ })[0]);
  expect((screen.getByLabelText(/^Ends at/) as HTMLInputElement).value).toBe('now');
  fireEvent.change(screen.getByLabelText('Window (minutes)'), { target: { value: '120' } });
  await waitFor(() =>
    expect(
      vi.mocked(api.validateDraft).mock.calls.some((call) => call[0].inputs[0].history?.windowMinutes === 120),
    ).toBe(true),
  );

  await user.click(screen.getAllByRole('button', { name: /IsHigh/ })[0]);
  fireEvent.change(screen.getByLabelText('Row 1: when'), { target: { value: 'Casing >= Limit' } });
  await waitFor(() =>
    expect(
      vi.mocked(api.validateDraft).mock.calls.some((call) => call[0].steps[0].rules?.[0].when === 'Casing >= Limit'),
    ).toBe(true),
  );
  await user.click(screen.getByRole('button', { name: 'Run test' }));
  await screen.findByText('Rule 1: Above the limit');
  await user.click(screen.getByRole('button', { name: 'Save this run as a stored example' }));
  await waitFor(() =>
    expect(
      vi
        .mocked(api.validateDraft)
        .mock.calls.some(
          (call) =>
            call[0].tests?.length === 1 &&
            call[0].tests[0].inputs.Casing === '300' &&
            call[0].tests[0].expect.High_out === 'true',
        ),
    ).toBe(true),
  );
});

it('lists the module as reads, decides and does, and opens a row in place', async () => {
  vi.mocked(api.loadStarters).mockResolvedValue([{ id: 'threshold', title: 'Pressure status', summary: '', draft }]);
  vi.mocked(connections.loadConnections).mockResolvedValue([]);
  vi.mocked(api.validateDraft).mockResolvedValue(analysis('one'));
  render(<AuthoringWorkbench />);
  const user = userEvent.setup();

  await user.click(await screen.findByRole('button', { name: /Pressure status|Gated/ }));
  await screen.findByRole('heading', { name: 'How it decides' });
  expect(screen.getByRole('heading', { name: 'What it reads' })).toBeTruthy();
  expect(screen.getByRole('heading', { name: 'What it does' })).toBeTruthy();
  expect(screen.queryByLabelText('Expression')).toBeNull();
  await waitFor(() =>
    expect(screen.getAllByRole('button', { name: /IsHigh/ })[0].textContent).toContain('reads Casing, Limit'),
  );
  const row = screen.getAllByRole('button', { name: /IsHigh/ })[0];
  await user.click(row);
  expect(row.getAttribute('aria-expanded')).toBe('true');
  expect(screen.getByLabelText('Expression')).toBeTruthy();
  await user.click(row);
  expect(screen.queryByLabelText('Expression')).toBeNull();
});
