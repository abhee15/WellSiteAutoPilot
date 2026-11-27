import { fireEvent, render, screen } from '@testing-library/react';
import { expect, test, vi } from 'vitest';
import { listGuidedModuleDrafts } from '../../src/api/generated/v1/sdk.gen';
import { AuthoringDrafts } from '../../src/views/AuthoringDrafts';

vi.mock('../../src/api/generated/v1/sdk.gen', () => ({ listGuidedModuleDrafts: vi.fn() }));

test('lists saved drafts with a link to open each one', async () => {
  vi.mocked(listGuidedModuleDrafts).mockResolvedValue({
    data: [
      {
        id: 'd1',
        revision: 2,
        manifestJson: JSON.stringify({ displayName: 'SPM optimizer', requiredAdapterKey: 'foresite' }),
        rulesJson: '{}',
        submitted: false,
        updatedAtUtc: '2026-11-26T12:00:00Z',
      },
    ],
  } as never);
  render(<AuthoringDrafts />);
  const link = await screen.findByRole('link', { name: 'SPM optimizer' });
  expect(link.getAttribute('href')).toBe('#authoring/draft%3Ad1');
  expect(screen.getByText('foresite')).toBeTruthy();
  expect(screen.getByText('Editing')).toBeTruthy();
});

test('says so when there are no saved drafts', async () => {
  vi.mocked(listGuidedModuleDrafts).mockResolvedValue({ data: [] } as never);
  render(<AuthoringDrafts />);
  expect(await screen.findByText(/no saved drafts yet/i)).toBeTruthy();
});
const draft = (index: number) => ({
  id: 'd' + index,
  revision: 1,
  manifestJson: JSON.stringify({ displayName: 'Module ' + index }),
  submitted: false,
  updatedAtUtc: '2026-11-26T12:00:00Z',
});

test('shows more drafts on request and retries a failed load in place', async () => {
  vi.mocked(listGuidedModuleDrafts)
    .mockResolvedValueOnce({ data: Array.from({ length: 25 }, (_, i) => draft(i)) } as never)
    .mockResolvedValueOnce({ data: undefined } as never)
    .mockResolvedValueOnce({ data: [draft(25)] } as never);
  render(<AuthoringDrafts />);
  fireEvent.click(await screen.findByRole('button', { name: 'Show more drafts' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Try again' }));
  expect(await screen.findByRole('link', { name: 'Module 25' })).toBeTruthy();
  expect(screen.getByRole('link', { name: 'Module 0' })).toBeTruthy();
  expect(screen.queryByRole('button', { name: 'Show more drafts' })).toBeNull();
});
