import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AuthoringWorkspace } from '../../src/views/AuthoringWorkspace';
import * as sdk from '../../src/api/generated/v1/sdk.gen';

vi.mock('../../src/api/generated/v1/sdk.gen', () => ({
  getAuthoringCapabilities: vi.fn(), listAuthoringDrafts: vi.fn(), createAuthoringDraft: vi.fn(),
  getAuthoringDraft: vi.fn(), updateAuthoringDraft: vi.fn(), validateAuthoringDraft: vi.fn(),
  buildAuthoringDraft: vi.fn(), cancelAuthoringDraftBuild: vi.fn(), downloadAuthoringDraftPackage: vi.fn(),
  addAuthoringDraftToCatalog: vi.fn(),
}));
const api = vi.mocked(sdk);
const ok = (data: unknown, status = 200) => ({ data, response: new Response(null, { status }) }) as never;

const document = {
  schema: 'x', profile: 'p', module: { moduleId: 'authored.demo', version: '1.0.0', displayName: 'Demo', publisher: 'P' },
  inputs: [{ id: 'level', label: 'Level', quantity: 'q', assetRole: 'asset' }], parameters: [],
  steps: [{ id: 'twice', label: 'Twice', kind: 'calculation', operation: 'multiply',
    operands: [{ kind: 'input', ref: 'level' }, { kind: 'literal', value: '2' }] }],
  conditions: [], outputs: [], actions: [],
};
const draft = (extra = {}) => ({
  id: '11111111-1111-4111-8111-111111111111', revision: 1, document, validationState: 'NotRun', testState: 'NotRun',
  buildState: 'None', validationIssues: [], ...extra,
});
const capabilities = { localBuildEnabled: true, starters: [{ id: 'threshold-alert', label: 'Threshold alert' }] };

beforeEach(() => {
  Object.values(api).forEach(mock => vi.mocked(mock as never as ReturnType<typeof vi.fn>).mockReset());
  api.getAuthoringCapabilities.mockResolvedValue(ok(capabilities));
  api.listAuthoringDrafts.mockResolvedValue(ok([]));
});
afterEach(cleanup);

it('explains the gate when local authoring is off', async () => {
  api.getAuthoringCapabilities.mockResolvedValue(ok({ localBuildEnabled: false }));
  render(<AuthoringWorkspace onCatalogChanged={vi.fn()} />);
  expect((await screen.findByText('Authoring is turned off')).textContent).toBeTruthy();
  expect(api.listAuthoringDrafts).not.toHaveBeenCalled();
});

it('creates a draft from a starter, blocks actions on edits, then builds and adds to the catalog', async () => {
  const user = userEvent.setup();
  api.createAuthoringDraft.mockResolvedValue(ok(draft(), 201));
  render(<AuthoringWorkspace onCatalogChanged={vi.fn()} />);
  await user.click(await screen.findByRole('button', { name: 'New draft' }));
  expect(api.createAuthoringDraft.mock.calls[0][0].body).toEqual({ starterId: 'threshold-alert' });
  expect(await screen.findByRole('heading', { name: 'Demo' })).toBeTruthy();

  api.validateAuthoringDraft.mockResolvedValue(ok(draft({ validationState: 'Valid', validatedRevision: 1 })));
  await user.click(screen.getByRole('button', { name: 'Validate' }));
  await waitFor(() => expect(screen.getByText('Valid')).toBeTruthy());

  await user.click(screen.getByRole('button', { name: 'Twice' }));
  const label = screen.getByLabelText('Label');
  await user.type(label, '!');
  expect(screen.getByRole('button', { name: 'Validate' }).matches(':disabled')).toBe(true);
  expect(screen.getByRole('button', { name: 'Build package' }).matches(':disabled')).toBe(true);

  api.updateAuthoringDraft.mockResolvedValue(ok(draft({ revision: 2, validationState: 'Stale' })));
  await user.click(screen.getByRole('button', { name: 'Save draft' }));
  expect(api.updateAuthoringDraft.mock.calls[0][0].body.expectedRevision).toBe(1);
  await waitFor(() => expect(screen.getByText('Out of date')).toBeTruthy());

  const built = draft({
    revision: 2, buildState: 'Succeeded',
    build: { revision: 2, state: 'Succeeded', packageSha256: 'a'.repeat(64), packageBytes: 10 },
  });
  api.buildAuthoringDraft.mockResolvedValue(ok(built, 202));
  await user.click(screen.getByRole('button', { name: 'Build package' }));
  const add = await screen.findByRole('button', { name: 'Add to module catalog' });
  expect(add.matches(':disabled')).toBe(false);

  api.addAuthoringDraftToCatalog.mockResolvedValue(ok({ moduleId: 'authored.demo', version: '1.0.0' }, 201));
  api.getAuthoringDraft.mockResolvedValue(ok({ ...built, build: { ...built.build, catalogModuleRecordId: 'x' } }));
  await user.click(add);
  await waitFor(() => expect(screen.getByRole('status').textContent).toContain('untrusted and disabled'));
});

it('reports a revision conflict without discarding the local edit', async () => {
  const user = userEvent.setup();
  api.listAuthoringDrafts.mockResolvedValue(ok([{ id: draft().id, displayName: 'Demo', moduleId: 'authored.demo', version: '1.0.0' }]));
  api.getAuthoringDraft.mockResolvedValue(ok(draft()));
  render(<AuthoringWorkspace onCatalogChanged={vi.fn()} />);
  await user.click(await screen.findByRole('button', { name: 'Open draft Demo' }));
  await user.click(await screen.findByRole('button', { name: 'Level' }));
  await user.type(screen.getByLabelText('Label'), '!');
  api.updateAuthoringDraft.mockResolvedValue({ error: { code: 'AUTHORING_REVISION_CONFLICT' }, response: new Response(null, { status: 409 }) } as never);
  await user.click(screen.getByRole('button', { name: 'Save draft' }));
  expect((await screen.findByRole('alert')).textContent).toContain('changed elsewhere');
  expect((screen.getByLabelText('Label') as HTMLInputElement).value).toBe('Level!');
});
