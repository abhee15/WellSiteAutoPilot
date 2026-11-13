import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { GuidedAuthoringPage } from '../../src/views/GuidedAuthoringPage';
import { UnsavedChangesPrompt } from '../../src/components/UnsavedChangesPrompt';
import {
  approveGuidedModuleSubmission,
  getGuidedModuleSubmission,
  createGuidedModuleDraft,
  listGuidedModuleDrafts,
  previewGuidedModuleDraft,
  submitGuidedModuleDraft,
} from '../../src/api/generated/v1/sdk.gen';

afterEach(cleanup);

vi.mock('../../src/api/generated/v1/sdk.gen', () => ({
  approveGuidedModuleSubmission: vi.fn(),
  createGuidedModuleDraft: vi.fn(),
  editGuidedModuleDraft: vi.fn(),
  getGuidedModuleSubmission: vi.fn(),
  listGuidedModuleDrafts: vi.fn(),
  previewGuidedModuleDraft: vi.fn(),
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
  expect(decodeURIComponent(new URL(link.value).hash)).toBe('#logic/authoring:draft-1/1');
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
  fireEvent.click(screen.getByRole('button', { name: 'Open submitted revision' }));
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
