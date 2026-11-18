import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { GuidedAuthoringPage, ModuleAuthoringWorkspace } from '../../src/views/GuidedAuthoringPage';
import { GuidedLivePreview } from '../../src/views/GuidedLivePreview';
import { UnsavedChangesPrompt } from '../../src/components/UnsavedChangesPrompt';
import {
  approveGuidedModuleSubmission,
  checkGuidedModuleAssetCompatibility,
  discoverGuidedModuleQuantities,
  getGuidedModuleSubmission,
  createGuidedModuleDraft,
  deleteGuidedModuleDraft,
  editGuidedModuleDraft,
  listGuidedModuleDrafts,
  listGuidedModuleProviders,
  listPendingGuidedModuleSubmissions,
  previewGuidedModuleDraft,
  previewGuidedModuleWithLiveInputs,
  previewGuidedModuleInputHistory,
  previewGuidedModuleProductionDays,
  previewGuidedModuleSurfaceCards,
  submitGuidedModuleDraft,
} from '../../src/api/generated/v1/sdk.gen';

afterEach(cleanup);

vi.mock('../../src/api/generated/v1/sdk.gen', () => ({
  approveGuidedModuleSubmission: vi.fn(),
  checkGuidedModuleAssetCompatibility: vi.fn(),
  discoverGuidedModuleQuantities: vi.fn(),
  createGuidedModuleDraft: vi.fn(),
  deleteGuidedModuleDraft: vi.fn(),
  editGuidedModuleDraft: vi.fn(),
  getGuidedModuleSubmission: vi.fn(),
  listGuidedModuleDrafts: vi.fn(),
  listGuidedModuleProviders: vi.fn(),
  listPendingGuidedModuleSubmissions: vi.fn(),
  previewGuidedModuleDraft: vi.fn(),
  previewGuidedModuleWithLiveInputs: vi.fn(),
  previewGuidedModuleInputHistory: vi.fn(),
  previewGuidedModuleProductionDays: vi.fn(),
  previewGuidedModuleSurfaceCards: vi.fn(),
  submitGuidedModuleDraft: vi.fn(),
}));

beforeEach(() => {
  vi.mocked(listGuidedModuleProviders).mockResolvedValue({
    data: [
      { adapterKey: 'foresite', displayName: 'ForeSite', currentValuesOnly: false },
      { adapterKey: 'cygnet', displayName: 'CygNet', currentValuesOnly: true },
    ],
  } as never);
});

it('saves, previews and submits the same guided draft revision', async () => {
  vi.mocked(listGuidedModuleDrafts).mockResolvedValue({ data: [] } as never);
  vi.mocked(createGuidedModuleDraft).mockResolvedValue({
    data: {
      id: 'draft-1',
      revision: 1,
      manifestJson: '{"displayName":"Fillage check","entryPoint":"wsa.declarative.threshold.v1"}',
      definition: { default: 70 },
    },
  } as never);
  vi.mocked(previewGuidedModuleDraft)
    .mockResolvedValueOnce({ data: { outcomeCode: 'LOW_FILLAGE' } } as never)
    .mockResolvedValueOnce({ data: { outcomeCode: 'THRESHOLD_NOT_MET' } } as never)
    .mockResolvedValueOnce({ data: { failureCode: 'MODULE_DECLARATIVE_INPUT_UNAVAILABLE' } } as never);
  vi.mocked(submitGuidedModuleDraft).mockResolvedValue({ data: { revision: 1 } } as never);
  render(<GuidedAuthoringPage canAuthor canReview={false} canApprove={false} onRefresh={async () => {}} />);
  fireEvent.change(screen.getByLabelText('Module name'), { target: { value: 'Fillage check' } });
  fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));
  await screen.findByText('Draft saved.');
  expect(screen.getByRole('region', { name: 'Sample cases' })).toBeTruthy();
  expect(document.querySelector('.guided-authoring-workbench')?.getAttribute('data-inspect-only')).toBe('false');
  fireEvent.click(screen.getByRole('button', { name: 'Run sample cases' }));
  expect((await screen.findByTitle('LOW_FILLAGE')).textContent).toBe('Low pump fillage');
  expect(screen.getByText('No recommendation')).toBeTruthy();
  expect(screen.getByText('Required reading unavailable')).toBeTruthy();
  fireEvent.click(screen.getByRole('button', { name: 'Submit for review' }));
  const link = await screen.findByLabelText<HTMLInputElement>('Shareable review link');
  expect(decodeURIComponent(new URL(link.value).hash)).toBe('#authoring/draft-1/1');
  expect(submitGuidedModuleDraft).toHaveBeenCalledWith({
    path: { id: 'draft-1' },
    body: { expectedRevision: 1, readings: [62, 70, null] },
  });
});

it('keeps unknown complex module calculations read-only in the guided editor', async () => {
  vi.mocked(listGuidedModuleDrafts).mockResolvedValue({
    data: [
      {
        id: 'complex-draft',
        revision: 1,
        manifestJson: JSON.stringify({
          displayName: 'Surface-card calibration',
          entryPoint: 'sample.structured-card-calibration.v2',
          dataRequirements: [{ id: 'cards', quantity: 'SurfaceCards' }],
        }),
        definition: { default: 70 },
      },
    ],
  } as never);
  render(<GuidedAuthoringPage canAuthor canReview={false} canApprove={false} onRefresh={async () => {}} />);
  await screen.findByRole('option', { name: 'Surface-card calibration' });
  fireEvent.change(screen.getByLabelText('Open draft'), { target: { value: 'complex-draft' } });
  const contract = document.querySelector<HTMLDetailsElement>('.guided-authoring-contract');
  expect(contract?.open).toBe(true);
  expect(screen.getByText(/View-only complex module/i)).toBeTruthy();
  expect(screen.queryByLabelText('Pump fillage threshold (%)')).toBeNull();
  expect(screen.queryByRole('button', { name: 'Save draft' })).toBeNull();
  expect(screen.queryByRole('button', { name: 'Submit for review' })).toBeNull();
  expect(screen.queryByLabelText('Module name')).toBeNull();
  expect(screen.queryByRole('button', { name: 'Run sample cases' })).toBeNull();
  expect(screen.queryByRole('region', { name: 'Sample cases' })).toBeNull();
  expect(screen.queryByRole('heading', { name: 'Rule' })).toBeNull();
  expect(document.querySelector('.guided-authoring-workbench')?.getAttribute('data-inspect-only')).toBe('true');
  fireEvent.click(screen.getByRole('tab', { name: 'IDE' }));
  expect(screen.queryByRole('navigation', { name: 'Outline' })).toBeNull();
  expect(screen.queryByRole('button', { name: 'Calculations' })).toBeNull();
  expect(screen.queryByRole('region', { name: 'Sample cases' })).toBeNull();
  expect(screen.getByText(/View-only complex module/i)).toBeTruthy();
});

it('keeps draft edits and the selected trace when switching between Workflow and IDE', async () => {
  vi.mocked(listGuidedModuleDrafts).mockResolvedValue({
    data: [
      {
        id: 'editor-draft',
        revision: 3,
        manifestJson: '{"displayName":"Shared draft","entryPoint":"wsa.declarative.threshold.v1"}',
        definition: { default: 70 },
      },
    ],
  } as never);
  vi.mocked(previewGuidedModuleDraft)
    .mockResolvedValueOnce({ data: { outcomeCode: 'LOW_FILLAGE' } } as never)
    .mockResolvedValueOnce({ data: { outcomeCode: 'THRESHOLD_NOT_MET' } } as never)
    .mockResolvedValueOnce({ data: { failureCode: 'MODULE_DECLARATIVE_INPUT_UNAVAILABLE' } } as never);
  render(<GuidedAuthoringPage canAuthor canReview={false} canApprove={false} onRefresh={async () => {}} />);
  vi.mocked(editGuidedModuleDraft).mockResolvedValue({
    data: {
      id: 'editor-draft',
      revision: 4,
      manifestJson: '{"displayName":"Shared draft","entryPoint":"wsa.declarative.threshold.v1"}',
      definition: { default: 72 },
    },
  } as never);
  const workflow = screen.getByRole('tab', { name: 'Workflow', selected: true });
  const ide = screen.getByRole('tab', { name: 'IDE', selected: false });
  expect(workflow.getAttribute('aria-controls')).toBe(screen.getByRole('tabpanel', { name: 'Workflow' }).id);
  await screen.findByRole('option', { name: 'Shared draft' });
  expect(screen.queryByRole('button', { name: 'Shared draft' })).toBeNull();
  fireEvent.change(screen.getByLabelText('Open draft'), { target: { value: 'editor-draft' } });
  fireEvent.click(screen.getByRole('button', { name: 'Run sample cases' }));
  await screen.findByText('No recommendation');

  workflow.focus();
  fireEvent.keyDown(workflow, { key: 'ArrowRight' });
  expect(document.activeElement).toBe(ide);
  expect(screen.getByRole('tab', { selected: true })).toBe(workflow);
  fireEvent.click(ide);
  expect(screen.getByRole('tabpanel', { name: 'IDE' }).id).toBe(ide.getAttribute('aria-controls'));
  expect(screen.getByRole('navigation', { name: 'Outline' })).toBeTruthy();
  fireEvent.change(screen.getByLabelText('Scenario'), { target: { value: '1' } });
  expect(screen.getByText('No recommendation')).toBeTruthy();
  expect(screen.queryByText('Low pump fillage')).toBeNull();

  fireEvent.click(screen.getByRole('button', { name: 'Rule' }));
  const threshold = screen.getByLabelText<HTMLInputElement>('Pump fillage threshold (%)');
  fireEvent.change(threshold, { target: { value: '72' } });
  fireEvent.click(workflow);
  expect(screen.getByLabelText('Pump fillage threshold (%)')).toBe(threshold);
  expect(threshold.value).toBe('72');
  expect(screen.getByText(/save the draft and rerun/i)).toBeTruthy();
  expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Submit for review' }).disabled).toBe(true);

  fireEvent.change(threshold, { target: { value: '70' } });
  fireEvent.click(ide);
  expect(screen.getByRole('button', { name: 'Rule', pressed: true })).toBeTruthy();
  expect(screen.getByLabelText<HTMLSelectElement>('Scenario').value).toBe('1');
  expect(screen.getByText('No recommendation')).toBeTruthy();

  fireEvent.change(threshold, { target: { value: '72' } });
  fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));
  await screen.findByText('Draft saved.');
  expect(screen.getByRole('button', { name: 'Rule', pressed: true })).toBeTruthy();
  expect(screen.getByLabelText<HTMLInputElement>('Pump fillage threshold (%)').value).toBe('72');
  expect(screen.queryByText('No recommendation')).toBeNull();
});

it('approves the immutable package shown to the reviewer', async () => {
  const packageSha256 = 'a'.repeat(64);
  vi.mocked(getGuidedModuleSubmission).mockResolvedValue({
    data: {
      draftId: 'draft-2',
      revision: 3,
      packageSha256,
      definition: { default: 65, title: 'Low pump fillage' },
      cases: [{ value: 60, outcomeCode: 'LOW_FILLAGE' }],
    },
  } as never);
  vi.mocked(approveGuidedModuleSubmission).mockResolvedValue({ data: { isEnabled: true } } as never);
  const refresh = vi.fn(async () => {});
  render(
    <GuidedAuthoringPage
      canAuthor={false}
      canReview
      canApprove
      initialReviewReference="draft-2/3"
      onRefresh={refresh}
    />,
  );
  expect(screen.queryByRole('button', { name: 'Save draft' })).toBeNull();
  expect((await screen.findByTitle('LOW_FILLAGE')).textContent).toBe('Low pump fillage');
  fireEvent.change(screen.getByLabelText('Approval reason'), { target: { value: 'Reviewed sample evidence' } });
  fireEvent.click(screen.getByRole('button', { name: 'Approve module' }));
  await screen.findByText('Module approved. Publication continues in the module catalog.');
  expect(approveGuidedModuleSubmission).toHaveBeenCalledWith({
    path: { id: 'draft-2', revision: 3 },
    body: { packageSha256, reason: 'Reviewed sample evidence' },
  });
  expect(refresh).toHaveBeenCalledOnce();
});

it('preserves unsaved edits until the author confirms a draft switch', async () => {
  vi.mocked(listGuidedModuleDrafts).mockResolvedValue({
    data: [
      {
        id: 'draft-3',
        revision: 1,
        manifestJson: '{"displayName":"Existing draft","entryPoint":"wsa.declarative.threshold.v1"}',
        definition: { default: 65 },
      },
    ],
  } as never);
  render(
    <>
      <GuidedAuthoringPage canAuthor canReview={false} canApprove={false} onRefresh={async () => {}} />
      <UnsavedChangesPrompt />
    </>,
  );
  await screen.findByRole('option', { name: 'Existing draft' });
  const existing = screen.getByLabelText<HTMLSelectElement>('Open draft');
  const name = screen.getByLabelText<HTMLInputElement>('Module name');
  fireEvent.change(name, { target: { value: 'Unsaved module' } });
  fireEvent.change(existing, { target: { value: 'draft-3' } });
  fireEvent.click(screen.getByRole('button', { name: /stay/i }));
  expect(name.value).toBe('Unsaved module');
  fireEvent.change(existing, { target: { value: 'draft-3' } });
  fireEvent.click(screen.getByRole('button', { name: /leave/i }));
  expect(name.value).toBe('Existing draft');
});

it('deletes an unsubmitted draft only after confirmation', async () => {
  const saved = {
    id: 'draft-del',
    revision: 1,
    manifestJson: '{"displayName":"Scratch check","entryPoint":"wsa.declarative.threshold.v1"}',
    definition: { default: 70, outcomeCode: 'LOW_FILLAGE' },
  };
  vi.mocked(listGuidedModuleDrafts).mockResolvedValue({ data: [saved] } as never);
  vi.mocked(deleteGuidedModuleDraft).mockResolvedValue({ response: { ok: true } } as never);
  render(<GuidedAuthoringPage canAuthor canReview={false} canApprove={false} onRefresh={async () => {}} />);
  fireEvent.change(await screen.findByLabelText('Open draft'), { target: { value: 'draft-del' } });
  fireEvent.click(await screen.findByRole('button', { name: 'Delete draft' }));
  fireEvent.click(screen.getByRole('button', { name: 'Keep' }));
  expect(deleteGuidedModuleDraft).not.toHaveBeenCalled();

  fireEvent.click(screen.getByRole('button', { name: 'Delete draft' }));
  fireEvent.click(screen.getByRole('button', { name: 'Yes, delete' }));
  await screen.findByText('Draft deleted.');
  expect(deleteGuidedModuleDraft).toHaveBeenCalledWith({ path: { id: 'draft-del' }, query: { expectedRevision: 1 } });
  expect(screen.queryByRole('option', { name: 'Scratch check' })).toBeNull();
});

it('names what to fix when the server refuses a save, and stays generic for unknown codes', async () => {
  vi.mocked(listGuidedModuleDrafts).mockResolvedValue({ data: [] } as never);
  vi.mocked(createGuidedModuleDraft)
    .mockResolvedValueOnce({ error: { code: 'GUIDED_DRAFT_REVISION_CONFLICT' } } as never)
    .mockResolvedValueOnce({ error: { code: 'SOMETHING_NEW' } } as never);
  render(<GuidedAuthoringPage canAuthor canReview={false} canApprove={false} onRefresh={async () => {}} />);
  fireEvent.change(screen.getByLabelText('Module name'), { target: { value: 'Refused check' } });
  fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));
  expect(await screen.findByText(/changed elsewhere/i)).toBeTruthy();
  fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));
  expect(await screen.findByText(/could not be completed/i)).toBeTruthy();
});

it('saves authored sample cases and flags an outcome that contradicts the expectation', async () => {
  vi.mocked(listGuidedModuleDrafts).mockResolvedValue({ data: [] } as never);
  vi.mocked(createGuidedModuleDraft).mockImplementation((async (options: { body: { sampleCases: unknown } }) => ({
    data: {
      id: 'draft-cases',
      revision: 1,
      manifestJson: '{"displayName":"Fillage check","entryPoint":"wsa.declarative.threshold.v1"}',
      definition: { default: 70, outcomeCode: 'LOW_FILLAGE' },
      sampleCases: options.body.sampleCases,
    },
  })) as never);
  vi.mocked(previewGuidedModuleDraft)
    .mockResolvedValueOnce({ data: { outcomeCode: 'THRESHOLD_NOT_MET', observedOutcome: 'NONE' } } as never)
    .mockResolvedValue({ data: { outcomeCode: 'THRESHOLD_NOT_MET', observedOutcome: 'NONE' } } as never);
  vi.mocked(submitGuidedModuleDraft).mockResolvedValue({ data: { revision: 1 } } as never);
  render(<GuidedAuthoringPage canAuthor canReview={false} canApprove={false} onRefresh={async () => {}} />);
  fireEvent.change(screen.getByLabelText('Module name'), { target: { value: 'Fillage check' } });
  fireEvent.change(screen.getByLabelText('Case 1 name'), { target: { value: 'Dry tubing' } });
  fireEvent.change(screen.getByLabelText('Case 1 expected outcome'), { target: { value: 'LOW_FILLAGE' } });
  fireEvent.click(screen.getByRole('button', { name: 'Add case' }));
  expect(screen.getByLabelText<HTMLInputElement>('Case 4 name').value).toBe('Case 4');
  fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));
  await screen.findByText('Draft saved.');
  expect(createGuidedModuleDraft).toHaveBeenCalledWith({
    body: expect.objectContaining({
      sampleCases: expect.arrayContaining([
        expect.objectContaining({ name: 'Dry tubing', expectedOutcome: 'LOW_FILLAGE' }),
        expect.objectContaining({ name: 'Case 4', value: null, expectedOutcome: null }),
      ]),
    }),
  });
  fireEvent.click(screen.getByRole('button', { name: 'Run sample cases' }));
  expect(await screen.findByText('Does not match the expectation. Observed: NONE.')).toBeTruthy();
  fireEvent.click(screen.getByRole('button', { name: 'Submit for review' }));
  await screen.findByLabelText('Shareable review link');
  // Saved cases are the evidence of record, so no readings are posted with the submission.
  expect(submitGuidedModuleDraft).toHaveBeenLastCalledWith({
    path: { id: 'draft-cases' },
    body: { expectedRevision: 1 },
  });
});

it('authors paired pressure samples and shows the reviewed differential', async () => {
  const manifestJson = '{"displayName":"Pressure check","entryPoint":"wsa.declarative.difference.v1"}';
  vi.mocked(listGuidedModuleDrafts).mockResolvedValue({ data: [] } as never);
  vi.mocked(createGuidedModuleDraft).mockResolvedValue({
    data: { id: 'pressure-draft', revision: 1, manifestJson, definition: { default: 150 } },
  } as never);
  vi.mocked(previewGuidedModuleDraft)
    .mockResolvedValueOnce({ data: { differentialPsi: 0, outcomeCode: 'THRESHOLD_NOT_MET' } } as never)
    .mockResolvedValueOnce({ data: { differentialPsi: 150, outcomeCode: 'THRESHOLD_NOT_MET' } } as never)
    .mockResolvedValueOnce({ data: { differentialPsi: 151, outcomeCode: 'PRESSURE_DIFFERENTIAL_HIGH' } } as never)
    .mockResolvedValueOnce({ data: { failureCode: 'MODULE_DECLARATIVE_INPUT_UNAVAILABLE' } } as never);
  vi.mocked(getGuidedModuleSubmission).mockResolvedValue({
    data: {
      reviewedManifestJson: manifestJson,
      definition: { default: 150 },
      cases: [
        {
          pressure: { casingPsi: 400, tubingPsi: 120 },
          differentialPsi: 280,
          outcomeCode: 'PRESSURE_DIFFERENTIAL_HIGH',
        },
      ],
    },
  } as never);
  render(<GuidedAuthoringPage canAuthor canReview canApprove={false} onRefresh={async () => {}} />);
  fireEvent.click(screen.getByText('New module', { selector: 'summary' }));
  await screen.findByRole('option', { name: 'CygNet' });
  fireEvent.change(screen.getByLabelText('Provider for the new module'), { target: { value: 'cygnet' } });
  fireEvent.click(screen.getByRole('button', { name: 'New pressure differential module' }));
  expect(screen.getByLabelText<HTMLInputElement>('Maximum differential (psi)').value).toBe('150');
  fireEvent.change(screen.getByLabelText('Module name'), { target: { value: 'Pressure check' } });
  fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));
  await screen.findByText('Draft saved.');
  fireEvent.click(screen.getByRole('button', { name: 'Run sample cases' }));
  expect(await screen.findByText(/Differential 151 psi/)).toBeTruthy();
  fireEvent.click(screen.getByRole('button', { name: 'Submit for review' }));
  await screen.findByLabelText('Shareable review link');
  expect(submitGuidedModuleDraft).toHaveBeenLastCalledWith({
    path: { id: 'pressure-draft' },
    body: {
      expectedRevision: 1,
      pressureReadings: [
        { casingPsi: 120, tubingPsi: 120 },
        { casingPsi: 150, tubingPsi: 0 },
        { casingPsi: 151, tubingPsi: 0 },
        { casingPsi: null, tubingPsi: 120 },
      ],
    },
  });
  fireEvent.click(screen.getByRole('button', { name: 'Open submitted revision' }));
  expect(
    await screen.findByText(/Casing pressure 400 psi · Tubing pressure 120 psi · Differential 280 psi/),
  ).toBeTruthy();
});

it('lists the exact submitted revision for reviewers and opens its evidence', async () => {
  const id = '8f52644a-daa0-4d1b-8949-cb20942b4dc8';
  vi.mocked(listPendingGuidedModuleSubmissions).mockResolvedValue({
    data: [{ draftId: id, revision: 3, displayName: 'Submitted name', submittedAtUtc: '2026-11-12T15:00:00Z' }],
  } as never);
  vi.mocked(getGuidedModuleSubmission).mockResolvedValue({
    data: {
      draftId: id,
      revision: 3,
      reviewedManifestJson: '{"displayName":"Submitted name"}',
      definition: { default: 70 },
      cases: [{ value: 62, outcomeCode: 'LOW_FILLAGE' }],
    },
  } as never);
  window.history.replaceState(null, '', '#authoring');
  try {
    render(<ModuleAuthoringWorkspace permissions={['logic.authoring.review']} onRefresh={async () => {}} />);
    const pending = await screen.findByRole('link', { name: 'Submitted name · revision 3' });
    expect(vi.mocked(listPendingGuidedModuleSubmissions)).toHaveBeenLastCalledWith({
      query: { offset: 0, limit: 26 },
      signal: expect.any(AbortSignal) as AbortSignal,
    });
    expect(screen.queryByRole('button', { name: 'Save draft' })).toBeNull();
    window.history.replaceState(null, '', pending.getAttribute('href'));
    fireEvent(window, new HashChangeEvent('hashchange'));
    expect(await screen.findByRole('heading', { name: 'Submitted name' })).toBeTruthy();
    expect(vi.mocked(getGuidedModuleSubmission)).toHaveBeenLastCalledWith({
      path: { id, revision: 3 },
      signal: expect.any(AbortSignal) as AbortSignal,
    });
    expect(screen.queryByRole('button', { name: 'Approve module' })).toBeNull();
    expect(screen.getByRole('link', { name: 'Back to reviews' }).getAttribute('href')).toBe('#authoring/reviews');
  } finally {
    window.history.replaceState(null, '', window.location.pathname);
  }
});

it('treats no returned input checks as unverified compatibility rather than failure or success', async () => {
  vi.mocked(checkGuidedModuleAssetCompatibility).mockResolvedValue({
    data: { inputs: [] },
  } as never);
  const fetch = vi.fn(
    async () =>
      new Response(
        JSON.stringify([
          { id: 'asset-1', name: 'Sample rod pump', isActive: true, ownerConnectionId: 'source', path: [] },
        ]),
      ),
  );
  vi.stubGlobal('fetch', fetch);
  try {
    render(<GuidedLivePreview draftId="draft-no-checks" revision={1} inputs={[]} />);
    fireEvent.click(screen.getByText('Preview with a sample asset'));
    expect(screen.getByText(/currently supports imported wells/i)).toBeTruthy();
    fireEvent.change(screen.getByLabelText('Asset name or ID'), { target: { value: 'pump' } });
    fireEvent.click(screen.getByRole('button', { name: 'Find assets' }));
    const assets = await screen.findByRole('combobox', { name: 'Sample asset' });
    fireEvent.change(assets, { target: { value: 'asset-1' } });
    expect(await screen.findByText(/No input checks were returned for this asset/i)).toBeTruthy();
    expect(screen.queryByText(/satisfies all checked input requirements/i)).toBeNull();
    expect(screen.queryByText('Could not check this asset.')).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'Check again' }));
    await waitFor(() => expect(checkGuidedModuleAssetCompatibility).toHaveBeenCalledTimes(2));
    expect(screen.getByText(/No input checks were returned for this asset/i)).toBeTruthy();
    expect(checkGuidedModuleAssetCompatibility).toHaveBeenCalledWith({
      path: { id: 'draft-no-checks' },
      body: { expectedRevision: 1, assetId: 'asset-1' },
      signal: expect.any(AbortSignal) as AbortSignal,
    });
  } finally {
    vi.unstubAllGlobals();
  }
});

it('says an asset without one active source cannot be checked, instead of a generic failure', async () => {
  vi.mocked(checkGuidedModuleAssetCompatibility).mockResolvedValue({
    error: { code: 'GUIDED_LIVE_SOURCE_UNAVAILABLE' },
  } as never);
  const fetch = vi.fn(
    async () =>
      new Response(
        JSON.stringify([{ id: 'asset-9', name: 'Demo well', isActive: true, ownerConnectionId: 'source', path: [] }]),
      ),
  );
  vi.stubGlobal('fetch', fetch);
  try {
    render(<GuidedLivePreview draftId="draft-no-source" revision={1} inputs={[]} />);
    fireEvent.click(screen.getByText('Preview with a sample asset'));
    fireEvent.change(screen.getByLabelText('Asset name or ID'), { target: { value: 'demo' } });
    fireEvent.click(screen.getByRole('button', { name: 'Find assets' }));
    fireEvent.change(await screen.findByRole('combobox', { name: 'Sample asset' }), { target: { value: 'asset-9' } });
    expect(await screen.findByText(/no single active imported source/i)).toBeTruthy();
    expect(screen.queryByText('Could not check this asset.')).toBeNull();
    expect(screen.queryByRole('button', { name: 'Check again' })).toBeNull();
  } finally {
    vi.unstubAllGlobals();
  }
});

it('previews a saved revision with an imported well without enabling submission', async () => {
  vi.mocked(listGuidedModuleDrafts).mockResolvedValue({
    data: [
      {
        id: 'live-draft',
        revision: 3,
        manifestJson:
          '{"displayName":"Live draft","entryPoint":"wsa.declarative.threshold.v1","dataRequirements":[{"id":"fillage","quantity":"PumpFillage"}]}',
        definition: { default: 70 },
      },
    ],
  } as never);
  const fetch = vi.fn(
    async () =>
      new Response(
        JSON.stringify([
          { id: 'manual', name: 'Manual well', isActive: true },
          { id: 'other', name: 'Other imported well', isActive: true, ownerConnectionId: 'source' },
          { id: 'imported', name: 'Imported well', isActive: true, ownerConnectionId: 'source' },
        ]),
      ),
  );
  vi.stubGlobal('fetch', fetch);
  const compatibility = vi.mocked(checkGuidedModuleAssetCompatibility);
  let resolvePrevious: (response: Awaited<ReturnType<typeof checkGuidedModuleAssetCompatibility>>) => void = () => {};
  compatibility
    .mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          resolvePrevious = resolve;
        }),
    )
    .mockResolvedValueOnce({
      data: { inputs: [{ inputId: 'fillage', quantity: 'PumpFillage', status: 'Satisfied' }] },
    } as never);
  vi.mocked(previewGuidedModuleWithLiveInputs).mockResolvedValue({
    data: {
      outcomeCode: 'THRESHOLD_NOT_MET',
      readings: [
        {
          inputId: 'fillage',
          quantity: 'PumpFillage',
          value: 82,
          unit: '%',
          quality: 'Good',
          timestampUtc: '2026-11-12T15:00:00Z',
        },
      ],
    },
  } as never);
  try {
    render(
      <GuidedAuthoringPage canAuthor canReview={false} canApprove={false} canPreviewLive onRefresh={async () => {}} />,
    );
    await screen.findByRole('option', { name: 'Live draft' });
    fireEvent.change(screen.getByLabelText('Open draft'), { target: { value: 'live-draft' } });
    fireEvent.click(screen.getByText('Preview with a sample asset'));
    fireEvent.change(screen.getByLabelText('Asset name or ID'), { target: { value: 'well' } });
    fireEvent.click(screen.getByRole('button', { name: 'Find assets' }));
    const wells = await screen.findByRole('combobox', { name: 'Sample asset' });
    expect(screen.queryByRole('option', { name: 'Manual well' })).toBeNull();
    expect(compatibility).not.toHaveBeenCalled();
    fireEvent.change(wells, { target: { value: 'other' } });
    const previousRequest = compatibility.mock.lastCall?.[0];
    expect(previousRequest?.body.assetId).toBe('other');
    expect(screen.getByText('Checking this asset against the draft’s declared inputs…')).toBeTruthy();
    fireEvent.change(wells, { target: { value: 'imported' } });
    expect(previousRequest?.signal?.aborted).toBe(true);
    expect(compatibility).toHaveBeenLastCalledWith({
      path: { id: 'live-draft' },
      body: { expectedRevision: 3, assetId: 'imported' },
      signal: expect.any(AbortSignal) as AbortSignal,
    });
    expect(await screen.findByText('PumpFillage · Meets requirement')).toBeTruthy();
    await act(async () => {
      resolvePrevious({
        data: { inputs: [{ inputId: 'fillage', quantity: 'PumpFillage', status: 'Missing' }] },
      } as never);
    });
    expect(screen.queryByText('PumpFillage · Reading missing')).toBeNull();
    expect(screen.getByText('PumpFillage · Meets requirement')).toBeTruthy();
    const discover = vi.mocked(discoverGuidedModuleQuantities);
    const source = screen.getByLabelText('Quantity source');
    const trendCatalog = {
      supported: true,
      quantities: [
        { quantity: 'PumpFillage', unit: '%', status: 'Observed' },
        { quantity: 'SPM', unit: 'SPM', status: 'Observed' },
        { quantity: 'AveragePumpFillage', status: 'NoSamples' },
      ],
    };
    discover.mockResolvedValueOnce({ data: trendCatalog } as never);
    fireEvent.change(source, { target: { value: 'WellTrend' } });
    const fillage = await screen.findByRole('option', { name: 'PumpFillage (%)' });
    expect(screen.queryByRole('option', { name: 'AveragePumpFillage' })).toBeNull();
    const quantity = screen.getByLabelText<HTMLSelectElement>('Historical quantity');
    fireEvent.change(quantity, { target: { value: 'SPM' } });
    expect(screen.getByText('Add this quantity to the draft to preview its trend.')).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Load history' })).toBeNull();
    fireEvent.change(quantity, { target: { value: 'PumpFillage' } });
    expect(fillage).toBeTruthy();
    const discoveryRequest = discover.mock.lastCall?.[0].body;
    expect(discoveryRequest).toMatchObject({ expectedRevision: 3, assetId: 'imported', capability: 'WellTrend' });

    const history = vi.mocked(previewGuidedModuleInputHistory);
    history.mockResolvedValueOnce({
      data: {
        quantity: 'PumpFillage',
        truncated: true,
        samples: [{ value: 79, unit: '%', quality: 'Unknown', timestampUtc: '2026-11-12T15:00:00Z' }],
      },
    } as never);
    fireEvent.click(screen.getByRole('button', { name: 'Load history' }));
    expect(await screen.findByText(/79 % · Unknown/)).toBeTruthy();
    expect(history.mock.lastCall?.[0].body).toEqual({
      expectedRevision: 3,
      assetId: 'imported',
      inputId: 'fillage',
      maximumSamples: 100,
      startUtc: discoveryRequest?.startUtc,
      endUtc: discoveryRequest?.endUtc,
    });

    fireEvent.change(screen.getByLabelText('Window ends at'), { target: { value: 'site' } });
    expect(screen.queryByText(/79 % · Unknown/)).toBeNull();
    expect(quantity.value).toBe('');
    expect(quantity.disabled).toBe(true);
    fireEvent.change(screen.getByLabelText('Window end at the site'), { target: { value: '2025-11-02T02:00' } });
    discover.mockResolvedValueOnce({ error: { code: 'GUIDED_HISTORY_SITE_TIME_UNRESOLVED' } } as never);
    fireEvent.change(screen.getByLabelText('Site time zone'), { target: { value: 'America/Chicago' } });
    expect(await screen.findByText(/That time is skipped or repeated/)).toBeTruthy();
    discover.mockResolvedValueOnce({ data: trendCatalog } as never);
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    await screen.findByRole('option', { name: 'PumpFillage (%)' });
    fireEvent.change(quantity, { target: { value: 'PumpFillage' } });
    const siteWindow = { endLocal: '2025-11-02T02:00', timeZoneId: 'America/Chicago', durationMinutes: 240 };
    expect(discover.mock.lastCall?.[0].body).toMatchObject({ siteWindow });
    history.mockResolvedValueOnce({
      data: { quantity: 'PumpFillage', startUtc: '2025-11-02T04:00:00Z', endUtc: '2025-11-02T08:00:00Z', samples: [] },
    } as never);
    fireEvent.click(screen.getByRole('button', { name: 'Load history' }));
    expect(await screen.findByText('2025-11-02T08:00:00Z')).toBeTruthy();
    expect(history.mock.lastCall?.[0].body).toEqual({
      expectedRevision: 3,
      assetId: 'imported',
      inputId: 'fillage',
      maximumSamples: 100,
      siteWindow,
    });

    let finishOldWindow: (response: Awaited<ReturnType<typeof discoverGuidedModuleQuantities>>) => void = () => {};
    discover.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finishOldWindow = resolve;
        }),
    );
    const requestCount = discover.mock.calls.length;
    fireEvent.change(screen.getByLabelText('History duration'), { target: { value: '1' } });
    await screen.findByText('Finding available quantities for this asset…');
    await waitFor(() => expect(discover).toHaveBeenCalledTimes(requestCount + 1));
    const oldWindowRequest = discover.mock.lastCall?.[0];
    expect(quantity.disabled).toBe(true);

    discover.mockResolvedValueOnce({
      data: {
        supported: true,
        quantities: [
          { quantity: 'TotalFluidRateInferred', unit: 'STB/d', status: 'Observed' },
          { quantity: 'OilRateInferred', unit: 'STB/d', status: 'Observed' },
        ],
      },
    } as never);
    fireEvent.change(source, { target: { value: 'DailyAverages' } });
    expect(quantity.value).toBe('');
    expect(screen.queryByText('2025-11-02T08:00:00Z')).toBeNull();
    await screen.findByRole('option', { name: 'TotalFluidRateInferred (STB/d)' });
    expect(oldWindowRequest?.signal?.aborted).toBe(true);
    await act(async () => finishOldWindow({ data: trendCatalog } as never));
    expect(screen.queryByRole('option', { name: 'PumpFillage (%)' })).toBeNull();
    fireEvent.change(quantity, { target: { value: 'OilRateInferred' } });
    expect(screen.getByText('The daily series preview supports inferred total fluid rate.')).toBeTruthy();
    fireEvent.change(quantity, { target: { value: 'TotalFluidRateInferred' } });
    const dailyRequest = discover.mock.lastCall?.[0].body;
    const daily = vi.mocked(previewGuidedModuleProductionDays);
    daily.mockResolvedValueOnce({
      data: {
        unit: 'STB/d',
        retrievedAtUtc: '2026-11-12T15:00:00Z',
        days: [
          { start: '2026-11-01T05:00:00Z', end: '2026-11-02T05:59:59Z', value: 0 },
          { start: '2026-11-02T00:00:00-06:00', end: '2026-11-02T23:59:59-06:00', value: null },
        ],
      },
    } as never);
    fireEvent.click(screen.getByRole('button', { name: 'Read last 7 days of production' }));
    expect(await screen.findByText(/0 STB\/d/)).toBeTruthy();
    expect(screen.getByText('2026-11-02T00:00:00-06:00')).toBeTruthy();
    expect(daily.mock.lastCall?.[0].body).toEqual({
      expectedRevision: 3,
      assetId: 'imported',
      startUtc: dailyRequest?.startUtc,
      endUtc: dailyRequest?.endUtc,
    });

    discover.mockResolvedValueOnce({
      data: {
        supported: true,
        quantities: [{ quantity: 'Oil', status: 'Reported' }],
        wellTest: { sampleDate: '2025-02-01T07:00:00-06:00', classification: 'RejectedTest' },
      },
    } as never);
    fireEvent.change(source, { target: { value: 'WellTest' } });
    expect(await screen.findByText('Oil · Reported on this test')).toBeTruthy();
    expect(screen.getByText('RejectedTest')).toBeTruthy();
    expect(screen.queryByLabelText('Historical quantity')).toBeNull();
    expect(screen.queryByText(/0 STB\/d/)).toBeNull();
    discover.mockResolvedValueOnce({ data: { supported: true, evidenceStatus: 'ApplicabilityUnverified' } } as never);
    fireEvent.change(source, { target: { value: 'Surveillance' } });
    expect(await screen.findByText(/Surveillance quantity applicability awaits verification/)).toBeTruthy();
    expect(screen.queryByLabelText('Historical quantity')).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'Run live preview' }));
    expect(await screen.findByText(/PumpFillage · 82 % · Good/)).toBeTruthy();
    const draw = {
      clearRect: vi.fn(),
      beginPath: vi.fn(),
      moveTo: vi.fn(),
      lineTo: vi.fn(),
      stroke: vi.fn(),
    };
    const context = vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(draw as never);
    try {
      const current = {
        id: 'current',
        kind: 'CURRENT',
        capturedAtUtc: '2026-11-12T14:59:00Z',
        positionUnit: 'in',
        loadUnit: 'lb',
        position: [0, 12, 0],
        load: [2000, 3600, 2000],
      };
      vi.mocked(previewGuidedModuleSurfaceCards).mockResolvedValueOnce({
        data: {
          quality: 'Unknown',
          retrievedAtUtc: '2026-11-12T15:00:00Z',
          cards: [current, { ...current, id: 'full', kind: 'FULL', load: [2200, 4200, 2200] }],
        },
      } as never);
      const cards = screen.getByRole('button', { name: 'Read stored surface cards' });
      fireEvent.click(cards);
      expect(await screen.findByRole('img', { name: 'Surface card load by position' })).toBeTruthy();
      expect(screen.getByText(/Load 2000–3600 lb/)).toBeTruthy();
      expect(screen.getByText(/Quality Unknown/)).toBeTruthy();
      fireEvent.change(screen.getByLabelText('Surface card'), { target: { value: '1' } });
      expect(screen.getByText(/Load 2200–4200 lb/)).toBeTruthy();
      expect(draw.lineTo).toHaveBeenCalledTimes(4);
      vi.mocked(previewGuidedModuleSurfaceCards).mockResolvedValueOnce({ data: { cards: [] } } as never);
      fireEvent.click(cards);
      expect(await screen.findByText('The provider returned no stored surface cards.')).toBeTruthy();
      expect(screen.queryByRole('img', { name: 'Surface card load by position' })).toBeNull();
    } finally {
      context.mockRestore();
    }
    expect((screen.getByRole('button', { name: 'Submit for review' }) as HTMLButtonElement).disabled).toBe(true);
  } finally {
    vi.unstubAllGlobals();
  }
});
