import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ModulePackageUpload } from '../../src/views/ModulePackageUpload';
import { uploadLogicModulePackage } from '../../src/api/generated/v1/sdk.gen';

vi.mock('../../src/api/generated/v1/sdk.gen', () => ({ uploadLogicModulePackage: vi.fn() }));
const upload = vi.mocked(uploadLogicModulePackage);
const module = { id: '00000000-0000-4000-8000-000000000001', moduleId: 'sample', version: '1.0.0',
  packageSha256: 'a'.repeat(64), isEnabled: false, trustStatus: 'Untrusted' };
beforeEach(() => { upload.mockReset(); });
afterEach(cleanup);
it('offers upload only to module managers', () => {
  render(<ModulePackageUpload canManage={false} onUploaded={vi.fn()} />);
  expect(screen.queryByText('Upload module package')).toBeNull();
});
it('sends unchanged binary bytes through the existing client and leaves approval separate', async () => {
  upload.mockResolvedValue({ data: module, response: new Response(null, { status: 201 }) } as never);
  const onUploaded = vi.fn(); render(<ModulePackageUpload canManage onUploaded={onUploaded} />);
  const user = userEvent.setup(); await user.click(screen.getByText('Upload module package'));
  const file = new File(['original archive'], 'sample.wsamodule');
  await user.upload(screen.getByLabelText('Module archive (.wsamodule or .zip)'), file);
  await user.click(screen.getByRole('button', { name: 'Upload package' }));
  await waitFor(() => expect(onUploaded).toHaveBeenCalledWith(module));
  expect(upload).toHaveBeenCalledTimes(1);
  expect(upload.mock.calls[0][0].body).toBe(file);
  expect(upload.mock.calls[0][0].credentials).toBe('same-origin');
  expect(screen.getByRole('status').textContent).toContain('Review its approval state');
});
it('blocks oversized archives before sending bytes', async () => {
  render(<ModulePackageUpload canManage onUploaded={vi.fn()} />);
  const user = userEvent.setup(); await user.click(screen.getByText('Upload module package'));
  const file = new File(['large'], 'sample.zip'); Object.defineProperty(file, 'size', { value: 16 * 1024 * 1024 + 1 });
  await user.upload(screen.getByLabelText('Module archive (.wsamodule or .zip)'), file);
  expect(screen.getByRole('alert').textContent).toContain('16 MiB');
  expect(screen.getByRole('button', { name: 'Upload package' }).matches(':disabled')).toBe(true);
  expect(upload).not.toHaveBeenCalled();
});
it('shows immutable-version failures without echoing provider or server details', async () => {
  upload.mockResolvedValue({ error: { code: 'MODULE_VERSION_IMMUTABLE', detail: 'SECRET' }, response: new Response(null, { status: 409 }) } as never);
  const onUploaded = vi.fn(); render(<ModulePackageUpload canManage onUploaded={onUploaded} />);
  const user = userEvent.setup(); await user.click(screen.getByText('Upload module package'));
  await user.upload(screen.getByLabelText('Module archive (.wsamodule or .zip)'), new File(['archive'], 'sample.zip'));
  await user.click(screen.getByRole('button', { name: 'Upload package' }));
  expect((await screen.findByRole('alert')).textContent).toContain('Build a new version');
  expect(screen.queryByText('SECRET')).toBeNull(); expect(onUploaded).not.toHaveBeenCalled();
});
