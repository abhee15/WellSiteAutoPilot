import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { GuidedAuthoringPage } from '../../src/views/GuidedAuthoringPage';
import {
  createGuidedModuleDraft,
  editGuidedModuleDraft,
  listGuidedModuleDrafts,
  submitGuidedModuleDraft,
} from '../../src/api/generated/v1/sdk.gen';

afterEach(cleanup);

vi.mock('../../src/api/generated/v1/sdk.gen', () => ({
  approveGuidedModuleSubmission: vi.fn(),
  checkGuidedModuleAssetCompatibility: vi.fn(),
  discoverGuidedModuleQuantities: vi.fn(),
  createGuidedModuleDraft: vi.fn(),
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
  vi.mocked(listGuidedModuleDrafts).mockResolvedValue({ data: [] } as never);
  vi.mocked(createGuidedModuleDraft).mockImplementation((async ({ body }: { body: { manifestJson: string } }) => ({
    data: { id: 'contract-1', revision: 1, manifestJson: body.manifestJson },
  })) as never);
  render(<GuidedAuthoringPage canAuthor canReview={false} canApprove={false} onRefresh={async () => {}} />);
  startContract();

  // No logic exists, so no preview, sample-case or submit affordance is offered.
  expect(screen.getByText(/inputs and outputs only/i)).toBeTruthy();
  expect(screen.queryByRole('button', { name: 'Submit for review' })).toBeNull();
  expect(screen.queryByRole('region', { name: 'Sample cases' })).toBeNull();
  expect(screen.getByRole('button', { name: 'Save draft' }).hasAttribute('disabled')).toBe(true);

  fireEvent.change(screen.getByLabelText('Module name'), { target: { value: 'Card calibration' } });
  fireEvent.click(screen.getByRole('button', { name: 'Add input' }));
  fireEvent.change(screen.getByLabelText('Quantity'), { target: { value: 'SurfaceLoad' } });
  fireEvent.change(screen.getByLabelText('Unit'), { target: { value: 'lb' } });
  fireEvent.change(screen.getByLabelText('Maximum age (seconds)'), { target: { value: '90' } });
  fireEvent.click(screen.getByRole('button', { name: 'Add output' }));
  fireEvent.change(screen.getByLabelText('Quantity'), { target: { value: 'PeakLoad' } });
  fireEvent.change(screen.getByLabelText('Value type'), { target: { value: 'string' } });

  // Workflow and IDE edit the same unsaved contract.
  fireEvent.click(screen.getByRole('tab', { name: 'IDE' }));
  expect(screen.getByRole('button', { name: 'PeakLoad' })).toBeTruthy();
  fireEvent.click(screen.getByRole('button', { name: 'SurfaceLoad' }));
  expect((screen.getByLabelText('Unit') as HTMLInputElement).value).toBe('lb');
  fireEvent.click(screen.getByRole('tab', { name: 'Workflow' }));

  fireEvent.change(screen.getByLabelText('Quantity'), { target: { value: 'bad quantity' } });
  expect(screen.getByRole('button', { name: 'Save draft' }).hasAttribute('disabled')).toBe(true);
  fireEvent.change(screen.getByLabelText('Quantity'), { target: { value: 'SurfaceLoad' } });
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
    dataRequirements: { quantity: string; canonicalUnit: string; maximumAgeSeconds: number; valueSchema: { schema: { type: string } } }[];
    outputRequirements: { quantity: string; valueSchema: { schema: { type: string } } }[];
  };
  expect(manifest.displayName).toBe('Card calibration');
  expect(manifest.entryPoint).toBe('contract.unset');
  expect(manifest.dataRequirements).toMatchObject([
    { quantity: 'SurfaceLoad', canonicalUnit: 'lb', maximumAgeSeconds: 90, valueSchema: { schema: { type: 'number' } } },
  ]);
  expect(manifest.outputRequirements).toMatchObject([
    { quantity: 'PeakLoad', valueSchema: { schema: { type: 'string' } } },
  ]);
  expect(submitGuidedModuleDraft).not.toHaveBeenCalled();

  // After saving, the IDE shows the saved contract read-only and the draft is no longer dirty.
  fireEvent.click(screen.getByRole('tab', { name: 'IDE' }));
  expect(within(screen.getByRole('region', { name: 'Saved contract (read-only)' })).getByText(/contract\.unset/)).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Save draft' }).hasAttribute('disabled')).toBe(true);
});

it('saves edits to a reopened contract against its revision and keeps structured shapes as saved', async () => {
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
  const saved = JSON.parse(call.body.manifestJson) as typeof structured & { dataRequirements: { canonicalUnit?: string }[] };
  expect(saved.dataRequirements[0]).toMatchObject({
    canonicalUnit: 'count',
    valueSchema: structured.dataRequirements[0]!.valueSchema,
  });
  await screen.findByText('Draft saved.');
});
