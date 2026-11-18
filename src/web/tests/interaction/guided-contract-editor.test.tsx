import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { GuidedAuthoringPage } from '../../src/views/GuidedAuthoringPage';
import { loadAdapterOperations } from '../../src/views/adapterOperations';
import {
  createGuidedModuleDraft,
  editGuidedModuleDraft,
  listGuidedModuleDrafts,
  submitGuidedModuleDraft,
} from '../../src/api/generated/v1/sdk.gen';

afterEach(cleanup);

vi.mock('../../src/views/adapterOperations', () => ({ loadAdapterOperations: vi.fn() }));

const cardsSchema = '{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"}}},"minItems":1}';

function operation(
  capabilityId: string,
  quantity: string,
  unit: string | null,
  structuredSchemaId: string | null = null,
  structuredJsonSchema: string | null = null,
) {
  return { capability: 'CurrentData', capabilityId, quantity, unit, structuredSchemaId, structuredJsonSchema };
}

// Two providers that register overlapping scalar readings, plus one structured read that must not become a scalar choice.
function registerCatalog() {
  vi.mocked(loadAdapterOperations).mockResolvedValue([
    {
      adapterKey: 'foresite',
      displayName: 'ForeSite',
      operations: [
        operation('engineering.current', 'PumpFillage', '%'),
        operation('structured.current', 'SurfaceCards', null, 'foresite.surface-cards/v1', cardsSchema),
      ],
    },
    {
      adapterKey: 'cygnet',
      displayName: 'CygNet',
      operations: [
        operation('engineering.current', 'PumpFillage', '%'),
        operation('engineering.current', 'CasingPressure', 'psi'),
      ],
    },
  ] as never);
}

vi.mock('../../src/api/generated/v1/sdk.gen', () => ({
  approveGuidedModuleSubmission: vi.fn(),
  checkGuidedModuleAssetCompatibility: vi.fn(),
  discoverGuidedModuleQuantities: vi.fn(),
  createGuidedModuleDraft: vi.fn(),
  deleteGuidedModuleDraft: vi.fn(),
  editGuidedModuleDraft: vi.fn(),
  getGuidedModuleSubmission: vi.fn(),
  listGuidedModuleDrafts: vi.fn(),
  listPendingGuidedModuleSubmissions: vi.fn(),
  previewGuidedModuleDraft: vi.fn(),
  previewGuidedModuleWithLiveInputs: vi.fn(),
  previewGuidedModuleInputHistory: vi.fn(),
  previewGuidedModuleProductionDays: vi.fn(),
  previewGuidedModuleSurfaceCards: vi.fn(),
  submitGuidedModuleDraft: vi.fn(),
}));

function startContract() {
  fireEvent.click(screen.getByText('New module'));
  fireEvent.click(screen.getByRole('button', { name: 'New contract-only module' }));
}

it('authors, saves and reopens a typed contract without an asset, in Workflow and IDE', async () => {
  registerCatalog();
  vi.mocked(listGuidedModuleDrafts).mockResolvedValue({ data: [] } as never);
  vi.mocked(createGuidedModuleDraft).mockImplementation((async ({ body }: { body: { manifestJson: string } }) => ({
    data: { id: 'contract-1', revision: 1, manifestJson: body.manifestJson },
  })) as never);
  render(
    <GuidedAuthoringPage canAuthor canPreviewLive canReview={false} canApprove={false} onRefresh={async () => {}} />,
  );
  startContract();

  // No logic exists, so no preview, sample-case or submit affordance is offered.
  expect(screen.getByText(/inputs and outputs only/i)).toBeTruthy();
  expect(screen.queryByRole('button', { name: 'Submit for review' })).toBeNull();
  expect(screen.queryByRole('region', { name: 'Sample cases' })).toBeNull();
  expect(screen.getByRole('button', { name: 'Save draft' }).hasAttribute('disabled')).toBe(true);

  fireEvent.change(screen.getByLabelText('Module name'), { target: { value: 'Card calibration' } });
  fireEvent.click(screen.getByRole('button', { name: 'Add input' }));

  // A new port starts empty with a prompt, not an unsaveable placeholder name in an error state.
  expect(screen.getByLabelText<HTMLInputElement>('Quantity').value).toBe('');
  expect(screen.getByText('Choose a registered quantity, or type one.')).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Save draft' }).hasAttribute('disabled')).toBe(true);

  // A quantity no adapter registers cannot be saved, because it has no capability that could ever be satisfied.
  fireEvent.change(screen.getByLabelText('Quantity'), { target: { value: 'SurfaceLoad' } });
  expect(await screen.findByText(/No registered source provides this quantity/i)).toBeTruthy();
  fireEvent.click(screen.getByRole('button', { name: 'Add output' }));
  fireEvent.change(screen.getByLabelText('Quantity'), { target: { value: 'PeakLoad' } });
  fireEvent.change(screen.getByLabelText('Value type'), { target: { value: 'string' } });
  expect(screen.getByRole('button', { name: 'Save draft' }).hasAttribute('disabled')).toBe(true);
  fireEvent.click(screen.getByRole('button', { name: 'SurfaceLoad' }));

  // A registered quantity supplies its capability and unit, and names every provider that registers it.
  fireEvent.change(screen.getByLabelText('Quantity'), { target: { value: 'CasingPressure' } });
  expect((screen.getByLabelText('Provider capability') as HTMLInputElement).value).toBe('engineering.current');
  expect((screen.getByLabelText('Unit') as HTMLInputElement).value).toBe('psi');
  expect(screen.getByText('Registered by: CygNet')).toBeTruthy();
  fireEvent.change(screen.getByLabelText('Quantity'), { target: { value: 'PumpFillage' } });
  expect(screen.getByText('Registered by: ForeSite, CygNet')).toBeTruthy();
  fireEvent.change(screen.getByLabelText('Maximum age (seconds)'), { target: { value: '90' } });

  // Workflow and IDE edit the same unsaved contract.
  fireEvent.click(screen.getByRole('tab', { name: 'IDE' }));
  expect(screen.getByRole('button', { name: 'PeakLoad' })).toBeTruthy();
  fireEvent.click(screen.getByRole('button', { name: 'PumpFillage' }));
  expect((screen.getByLabelText('Unit') as HTMLInputElement).value).toBe('%');
  fireEvent.click(screen.getByRole('tab', { name: 'Workflow' }));

  fireEvent.change(screen.getByLabelText('Quantity'), { target: { value: 'bad quantity' } });
  expect(screen.getByRole('button', { name: 'Save draft' }).hasAttribute('disabled')).toBe(true);
  fireEvent.change(screen.getByLabelText('Quantity'), { target: { value: 'PumpFillage' } });
  fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));
  await screen.findByText('Draft saved.');

  const body = vi.mocked(createGuidedModuleDraft).mock.calls[0]![0].body as unknown as {
    manifestJson: string;
    definition?: unknown;
  };
  expect(body.definition).toBeUndefined();
  const manifest = JSON.parse(body.manifestJson) as {
    displayName: string;
    entryPoint: string;
    dataRequirements: {
      quantity: string;
      canonicalUnit: string;
      maximumAgeSeconds: number;
      requiredCapability: string;
      valueSchema: { schema: { type: string } };
    }[];
    outputRequirements: { quantity: string; valueSchema: { schema: { type: string } } }[];
  };
  expect(manifest.displayName).toBe('Card calibration');
  expect(manifest.entryPoint).toBe('contract.unset');
  expect(manifest.dataRequirements).toMatchObject([
    {
      quantity: 'PumpFillage',
      canonicalUnit: '%',
      maximumAgeSeconds: 90,
      requiredCapability: 'engineering.current',
      valueSchema: { schema: { type: 'number' } },
    },
  ]);
  expect(manifest.outputRequirements).toMatchObject([
    { quantity: 'PeakLoad', valueSchema: { schema: { type: 'string' } } },
  ]);
  expect(submitGuidedModuleDraft).not.toHaveBeenCalled();

  // Sample-asset inspection is available for the saved revision, but nothing can be run because there is no logic.
  fireEvent.click(screen.getByText('Preview with a sample asset'));
  expect(screen.getByLabelText('Asset name or ID')).toBeTruthy();
  expect(screen.queryByRole('button', { name: 'Run live preview' })).toBeNull();

  // After saving, the IDE shows the saved contract read-only and the draft is no longer dirty.
  fireEvent.click(screen.getByRole('tab', { name: 'IDE' }));
  expect(
    within(screen.getByRole('region', { name: 'Saved contract (read-only)' })).getByText(/contract\.unset/),
  ).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Save draft' }).hasAttribute('disabled')).toBe(true);
});

it("adds a structured input whose schema is the provider's own, not one built by hand", async () => {
  registerCatalog();
  vi.mocked(listGuidedModuleDrafts).mockResolvedValue({ data: [] } as never);
  vi.mocked(createGuidedModuleDraft).mockImplementation((async ({ body }: { body: { manifestJson: string } }) => ({
    data: { id: 'contract-cards', revision: 1, manifestJson: body.manifestJson },
  })) as never);
  render(<GuidedAuthoringPage canAuthor canReview={false} canApprove={false} onRefresh={async () => {}} />);
  startContract();
  fireEvent.change(screen.getByLabelText('Module name'), { target: { value: 'Card check' } });
  fireEvent.click(await screen.findByRole('button', { name: 'Add structured input: SurfaceCards' }));

  // The quantity belongs to the schema, so it cannot be retyped into something the provider does not serve.
  expect(screen.getByLabelText<HTMLInputElement>('Quantity').disabled).toBe(true);
  fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));
  await screen.findByText('Draft saved.');
  const body = vi.mocked(createGuidedModuleDraft).mock.calls.at(-1)![0].body as unknown as { manifestJson: string };
  expect(JSON.parse(body.manifestJson).dataRequirements).toMatchObject([
    {
      quantity: 'SurfaceCards',
      requiredCapability: 'structured.current',
      valueSchema: { schemaId: 'foresite.surface-cards/v1', schema: JSON.parse(cardsSchema) },
    },
  ]);
});

it('says when the source catalog is unavailable instead of inventing capabilities', async () => {
  vi.mocked(loadAdapterOperations).mockRejectedValue(new Error('denied'));
  vi.mocked(listGuidedModuleDrafts).mockResolvedValue({ data: [] } as never);
  render(<GuidedAuthoringPage canAuthor canReview={false} canApprove={false} onRefresh={async () => {}} />);
  startContract();
  fireEvent.change(screen.getByLabelText('Module name'), { target: { value: 'Offline catalog' } });
  fireEvent.click(screen.getByRole('button', { name: 'Add input' }));
  expect(await screen.findByText(/source catalog could not be loaded/i)).toBeTruthy();
  fireEvent.change(screen.getByLabelText('Quantity'), { target: { value: 'PumpFillage' } });
  expect((screen.getByLabelText('Provider capability') as HTMLInputElement).value).toBe('');
  expect(screen.getByRole('button', { name: 'Save draft' }).hasAttribute('disabled')).toBe(true);
});

it('saves edits to a reopened contract against its revision and keeps structured shapes as saved', async () => {
  registerCatalog();
  const structured = {
    manifestVersion: 2,
    moduleId: 'authoring.cards',
    version: '1.0.0',
    displayName: 'Cards',
    entryPoint: 'contract.unset',
    assetRequirements: [{ role: 'well', minimumCount: 1, maximumCount: 1, requiredTraits: [] }],
    dataRequirements: [
      {
        id: 'cards',
        assetRole: 'well',
        quantity: 'SurfaceCards',
        access: 'Current',
        requiredCapability: 'structured.current',
        valueSchema: { schemaId: 'foresite.surface-cards/v1', schema: { type: 'array', items: { type: 'object' } } },
      },
    ],
    outputRequirements: [],
  };
  vi.mocked(listGuidedModuleDrafts).mockResolvedValue({
    data: [{ id: 'contract-2', revision: 4, manifestJson: JSON.stringify(structured) }],
  } as never);
  vi.mocked(editGuidedModuleDraft).mockImplementation((async ({ body }: { body: { manifestJson: string } }) => ({
    data: { id: 'contract-2', revision: 5, manifestJson: body.manifestJson },
  })) as never);
  render(<GuidedAuthoringPage canAuthor canReview={false} canApprove={false} onRefresh={async () => {}} />);
  await screen.findByRole('option', { name: 'Cards' });
  fireEvent.change(screen.getByLabelText('Open draft'), { target: { value: 'contract-2' } });

  expect(await screen.findByText(/structured shape is kept as saved/i)).toBeTruthy();
  expect(screen.queryByLabelText('Value type')).toBeNull();
  fireEvent.change(screen.getByLabelText('Unit'), { target: { value: 'count' } });
  fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));
  await waitFor(() => expect(editGuidedModuleDraft).toHaveBeenCalled());
  const call = vi.mocked(editGuidedModuleDraft).mock.calls[0]![0] as unknown as {
    path: { id: string };
    body: { expectedRevision: number; manifestJson: string };
  };
  expect(call.path.id).toBe('contract-2');
  expect(call.body.expectedRevision).toBe(4);
  const saved = JSON.parse(call.body.manifestJson) as typeof structured & {
    dataRequirements: { canonicalUnit?: string }[];
  };
  expect(saved.dataRequirements[0]).toMatchObject({
    canonicalUnit: 'count',
    valueSchema: structured.dataRequirements[0]!.valueSchema,
  });
  await screen.findByText('Draft saved.');
});

it('lets an author choose where an input is read from, and saves it beside the contract', async () => {
  vi.mocked(loadAdapterOperations).mockResolvedValue([
    {
      adapterKey: 'foresite',
      displayName: 'ForeSite',
      operations: [
        { key: 'current.pump-fillage', displayName: 'Current pump fillage', ...operation('engineering.current', 'PumpFillage', '%') },
        { key: 'history.stroke-rate', displayName: 'Stroke rate trend', ...operation('engineering.history', 'SPM', 'SPM') },
      ],
    },
    {
      adapterKey: 'cygnet',
      displayName: 'CygNet',
      operations: [{ key: 'current.pump-fillage', displayName: 'CygNet fillage', ...operation('engineering.current', 'PumpFillage', '%') }],
    },
  ] as never);
  vi.mocked(listGuidedModuleDrafts).mockResolvedValue({ data: [] } as never);
  vi.mocked(createGuidedModuleDraft).mockImplementation((async ({ body }: { body: { manifestJson: string } }) => ({
    data: { id: 'contract-hints', revision: 1, manifestJson: body.manifestJson },
  })) as never);
  render(
    <GuidedAuthoringPage canAuthor canPreviewLive canReview={false} canApprove={false} onRefresh={async () => {}} />,
  );
  startContract();
  fireEvent.change(screen.getByLabelText('Module name'), { target: { value: 'Pinned fillage' } });
  fireEvent.click(screen.getByRole('button', { name: 'Add input' }));
  // The registry supplies the capability, so wait for it before choosing the quantity.
  await screen.findByLabelText('Read from provider');
  fireEvent.change(screen.getByLabelText('Quantity'), { target: { value: 'PumpFillage' } });

  // No choice means any provider that serves the quantity, so nothing is saved for the input.
  expect(screen.getByLabelText<HTMLSelectElement>('Read from provider').value).toBe('');
  expect(screen.getByLabelText('Only run with this source').hasAttribute('disabled')).toBe(true);
  fireEvent.change(screen.getByLabelText('Read from provider'), { target: { value: 'foresite' } });
  fireEvent.change(screen.getByLabelText('Read with operation'), { target: { value: 'current.pump-fillage' } });
  // Only operations that serve this quantity are offered.
  expect(screen.getByRole('option', { name: 'Current pump fillage' })).toBeTruthy();
  expect(screen.queryByRole('option', { name: 'Stroke rate trend' })).toBeNull();
  fireEvent.click(screen.getByLabelText('Only run with this source'));
  expect(screen.getByText(/limits which assets this module/i)).toBeTruthy();

  fireEvent.click(screen.getByRole('button', { name: 'Add output' }));
  fireEvent.change(screen.getByLabelText('Quantity'), { target: { value: 'Result' } });
  fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));
  await screen.findByText('Draft saved.');
  const body = vi.mocked(createGuidedModuleDraft).mock.calls.at(-1)![0].body as unknown as {
    sourceHints: { inputId: string; adapterKey: string; operationKey: string; required: boolean }[];
    manifestJson: string;
  };
  const manifest = JSON.parse(body.manifestJson) as { dataRequirements: { id: string }[] };
  expect(body.sourceHints).toEqual([
    {
      inputId: manifest.dataRequirements[0]!.id,
      adapterKey: 'foresite',
      operationKey: 'current.pump-fillage',
      required: true,
      parametersJson: null,
    },
  ]);
  // The hint is not part of the portable manifest.
  expect(body.manifestJson).not.toContain('current.pump-fillage');
});
