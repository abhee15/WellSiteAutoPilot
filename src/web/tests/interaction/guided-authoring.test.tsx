import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { GuidedAuthoringPage, ModuleAuthoringWorkspace } from '../../src/views/GuidedAuthoringPage';
import { UnsavedChangesPrompt } from '../../src/components/UnsavedChangesPrompt';
import {
  approveGuidedModuleSubmission,
  getGuidedModuleSubmission,
  createGuidedModuleDraft,
  listGuidedModuleDrafts,
  listPendingGuidedModuleSubmissions,
  previewGuidedModuleDraft,
  previewGuidedModuleWithLiveInputs,
  submitGuidedModuleDraft,
} from '../../src/api/generated/v1/sdk.gen';

afterEach(cleanup);

vi.mock('../../src/api/generated/v1/sdk.gen', () => ({
  approveGuidedModuleSubmission: vi.fn(),
  createGuidedModuleDraft: vi.fn(),
  editGuidedModuleDraft: vi.fn(),
  getGuidedModuleSubmission: vi.fn(),
  listGuidedModuleDrafts: vi.fn(),
  listPendingGuidedModuleSubmissions: vi.fn(),
  previewGuidedModuleDraft: vi.fn(),
  previewGuidedModuleWithLiveInputs: vi.fn(),
  submitGuidedModuleDraft: vi.fn(),
}));

it('saves, previews and submits the same guided draft revision', async () => {
  vi.mocked(listGuidedModuleDrafts).mockResolvedValue({ data: [] } as never);
  vi.mocked(createGuidedModuleDraft).mockResolvedValue({
    data: {
      id: 'draft-1',
      revision: 1,
      manifestJson: '{"displayName":"Fillage check"}',
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
        manifestJson: '{"displayName":"Existing draft"}',
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
  const existing = await screen.findByRole('button', { name: 'Existing draft' });
  const name = screen.getByLabelText<HTMLInputElement>('Module name');
  fireEvent.change(name, { target: { value: 'Unsaved module' } });
  fireEvent.click(existing);
  fireEvent.click(screen.getByRole('button', { name: /stay/i }));
  expect(name.value).toBe('Unsaved module');
  fireEvent.click(existing);
  fireEvent.click(screen.getByRole('button', { name: /leave/i }));
  expect(name.value).toBe('Existing draft');
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

it('previews a saved revision with an imported well without enabling submission', async () => {
  vi.mocked(listGuidedModuleDrafts).mockResolvedValue({
    data: [
      { id: 'live-draft', revision: 3, manifestJson: '{"displayName":"Live draft"}', definition: { default: 70 } },
    ],
  } as never);
  const fetch = vi.fn(
    async () =>
      new Response(
        JSON.stringify([
          { id: 'manual', name: 'Manual well', isActive: true },
          { id: 'imported', name: 'Imported well', isActive: true, ownerConnectionId: 'source' },
        ]),
      ),
  );
  vi.stubGlobal('fetch', fetch);
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
    fireEvent.click(await screen.findByRole('button', { name: 'Live draft' }));
    fireEvent.click(screen.getByText('Preview with a live well'));
    fireEvent.change(screen.getByLabelText('Well name or asset ID'), { target: { value: 'well' } });
    fireEvent.click(screen.getByRole('button', { name: 'Find wells' }));
    const wells = await screen.findByRole('combobox', { name: 'Sample well' });
    expect(screen.queryByRole('option', { name: 'Manual well' })).toBeNull();
    fireEvent.change(wells, { target: { value: 'imported' } });
    fireEvent.click(screen.getByRole('button', { name: 'Run live preview' }));
    expect(await screen.findByText(/PumpFillage · 82 % · Good/)).toBeTruthy();
    expect(vi.mocked(previewGuidedModuleWithLiveInputs)).toHaveBeenLastCalledWith({
      path: { id: 'live-draft' },
      body: { expectedRevision: 3, assetId: 'imported' },
      signal: expect.any(AbortSignal) as AbortSignal,
    });
    expect((screen.getByRole('button', { name: 'Submit for review' }) as HTMLButtonElement).disabled).toBe(true);
  } finally {
    vi.unstubAllGlobals();
  }
});
