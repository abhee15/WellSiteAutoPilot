import { render, screen } from '@testing-library/react';
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
      },
    ],
  } as never);
  render(<AuthoringDrafts />);
  const link = await screen.findByRole('link', { name: 'Open SPM optimizer' });
  expect(link.getAttribute('href')).toBe('#authoring/draft%3Ad1');
  expect(screen.getByText('Rules module · foresite')).toBeTruthy();
});

test('says so when there are no saved drafts', async () => {
  vi.mocked(listGuidedModuleDrafts).mockResolvedValue({ data: [] } as never);
  render(<AuthoringDrafts />);
  expect(await screen.findByText(/no saved drafts yet/i)).toBeTruthy();
});
