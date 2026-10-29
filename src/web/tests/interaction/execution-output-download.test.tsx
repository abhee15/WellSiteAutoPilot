import { afterEach, expect, test, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { ExecutionOutputDownload } from '../../src/views/ExecutionOutputDownload';
const id = 'd875c27a-8a9c-4f3d-85cb-24f097b956dc';
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

test('revoked access never downloads cached data and notifies the detail panel', async () => {
  const create = vi.fn(), onAccessLost = vi.fn();
  vi.stubGlobal('URL', { createObjectURL: create, revokeObjectURL: vi.fn() });
  vi.stubGlobal('fetch', vi.fn(async () => new Response('{}', { status: 403 })));
  render(<ExecutionOutputDownload executionId={id} onAccessLost={onAccessLost} />);
  fireEvent.click(screen.getByRole('button', { name: 'Download result JSON' }));
  await waitFor(() => expect(onAccessLost).toHaveBeenCalledOnce());
  expect(create).not.toHaveBeenCalled();
  expect(screen.getByRole('alert').textContent).toContain('could not be downloaded');
});

test('fresh result download uses a safe filename and releases the object URL', async () => {
  const create = vi.fn(() => 'blob:result'), revoke = vi.fn();
  vi.stubGlobal('URL', { createObjectURL: create, revokeObjectURL: revoke });
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ executionId: id, status: 'Completed', outputJson: '{"value":12345.67890123456789}' }))));
  let filename = '';
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) { filename = this.download; });
  render(<ExecutionOutputDownload executionId={id} />);
  fireEvent.click(screen.getByRole('button', { name: 'Download result JSON' }));
  await waitFor(() => expect(create).toHaveBeenCalledOnce());
  expect(filename).toBe(`execution-${id}.json`);
  expect(document.querySelector('a[download]')).toBeNull();
  await waitFor(() => expect(revoke).toHaveBeenCalledWith('blob:result'), { timeout: 2000 });
});

test('closing the panel cancels pending export and cannot produce a download', async () => {
  const create = vi.fn();
  vi.stubGlobal('URL', { createObjectURL: create, revokeObjectURL: vi.fn() });
  let signal: AbortSignal | undefined;
  vi.stubGlobal('fetch', vi.fn((_url, options) => { signal = options.signal; return new Promise(() => {}); }));
  const view = render(<ExecutionOutputDownload executionId={id} />);
  fireEvent.click(screen.getByRole('button', { name: 'Download result JSON' }));
  view.unmount();
  expect(signal?.aborted).toBe(true);
  expect(create).not.toHaveBeenCalled();
});
