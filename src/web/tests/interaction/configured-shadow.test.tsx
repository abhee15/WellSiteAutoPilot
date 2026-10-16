import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ConfiguredShadowPanel, shadowRequestStorageKey } from '../../src/views/ConfiguredShadowPanel';
import { ExecutionDetailContent } from '../../src/views/ExecutionDetailPanel';
const userId = '11111111-1111-4111-8111-111111111111';
const configurationId = '22222222-2222-4222-8222-222222222222';
const revisionId = '33333333-3333-4333-8333-333333333333';
const executionId = '44444444-4444-4444-8444-444444444444';
const savedKey = '55555555-5555-4555-8555-555555555555';
const revision = { id: revisionId, revisionNumber: 3, status: 'Active', mode: 'Shadow' };
const completed = { executionId, logicInstanceId: configurationId, configurationRevisionId: revisionId,
  requestContractVersion: 2, trigger: 'Manual', mode: 'Shadow', status: 'Completed' };
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status });
const storageKey = shadowRequestStorageKey(userId, configurationId, revisionId);
beforeEach(() => { sessionStorage.clear(); });
afterEach(() => { cleanup(); sessionStorage.clear(); vi.unstubAllGlobals(); vi.restoreAllMocks(); });
function panel(canRequest = true) {
  return render(<ConfiguredShadowPanel userId={userId} configurationId={configurationId}
    revision={revision} canRequest={canRequest} />);
}
async function confirm(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('button', { name: 'Review Shadow run' }));
  await user.click(screen.getByRole('button', { name: 'Run reviewed Shadow revision' }));
}
describe('configured Shadow execution', () => {
  it('requires a review, saves a key before posting, verifies scoped readback and inspects the result', async () => {
    const user = userEvent.setup();
    const http = vi.fn(async (_url: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        expect(JSON.parse(String(init.body)).requestId).toBe(sessionStorage.getItem(storageKey));
        return response({ executionId, logicInstanceId: configurationId, configurationRevisionId: revisionId,
          requestContractVersion: 2, mode: 'Shadow', trigger: 'Manual' });
      }
      return response(completed);
    });
    vi.stubGlobal('fetch', http); panel();
    await user.click(screen.getByRole('button', { name: 'Review Shadow run' }));
    expect(http).not.toHaveBeenCalled();
    await user.click(screen.getByRole('button', { name: 'Run reviewed Shadow revision' }));
    await screen.findByRole('region', { name: 'Execution details' });
    await screen.findByText('Completed');
    expect(http.mock.calls.filter(c => c[1]?.method === 'POST')).toHaveLength(1);
    expect(http.mock.calls[0][0]).toBe(`/api/v1/configured-logic/${configurationId}/revisions/${revisionId}/shadow`);
    expect(screen.getByRole('button', { name: 'Prepare another Shadow run' })).toBeTruthy();
  });
  it('recovers a lost response with exactly the same key and never retries automatically', async () => {
    const user = userEvent.setup(); let fail = true;
    const http = vi.fn(async (_url: string, init?: RequestInit) => {
      if (init?.method === 'POST' && fail) throw new TypeError('network');
      return response(completed);
    });
    vi.stubGlobal('fetch', http); const view = panel(); await confirm(user);
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', expect.stringContaining('outcome could not be verified'));
    const firstKey = JSON.parse(String(http.mock.calls[0][1]?.body)).requestId;
    expect(http).toHaveBeenCalledTimes(1);
    view.unmount(); fail = false; panel();
    await user.click(screen.getByRole('button', { name: 'Recover saved Shadow request' }));
    await screen.findByText('Completed');
    const posts = http.mock.calls.filter(c => c[1]?.method === 'POST');
    expect(posts).toHaveLength(2); expect(JSON.parse(String(posts[1][1]?.body)).requestId).toBe(firstKey);
  });
  it('refuses to POST when a recovery token cannot be persisted', async () => {
    const user = userEvent.setup(); const http = vi.fn(); vi.stubGlobal('fetch', http);
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('disabled'); });
    panel(); await confirm(user);
    expect(screen.getByRole('alert').textContent).toContain('No new request was sent'); expect(http).not.toHaveBeenCalled();
  });
  it('fails closed on corrupted recovery state instead of silently creating a new key', async () => {
    sessionStorage.setItem(storageKey, 'invalid'); const user = userEvent.setup(); const http = vi.fn(); vi.stubGlobal('fetch', http);
    panel(); await user.click(screen.getByRole('button', { name: 'Recover saved Shadow request' }));
    expect(screen.getByRole('alert').textContent).toContain('recovery key is invalid'); expect(http).not.toHaveBeenCalled();
  });
  it('does not load another user\'s saved key', () => {
    sessionStorage.setItem(shadowRequestStorageKey(executionId, configurationId, revisionId), savedKey);
    panel(); expect(screen.getByRole('button', { name: 'Review Shadow run' })).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Recover saved Shadow request' })).toBeNull();
  });
  it('uses secure random bytes when randomUUID is unavailable on an on-prem HTTP origin', async () => {
    const user = userEvent.setup();
    vi.stubGlobal('crypto', { getRandomValues: (bytes: Uint8Array) => { bytes.fill(42); return bytes; } });
    vi.stubGlobal('fetch', vi.fn(async () => response(completed))); panel(); await confirm(user);
    await screen.findByText('Completed'); expect(sessionStorage.getItem(storageKey)).toMatch(/^[0-9a-f-]{36}$/);
  });
  it('retains the same key on revision/overlap conflict, without reflecting arbitrary server details', async () => {
    const user = userEvent.setup(); vi.stubGlobal('fetch', vi.fn(async () => response({ code: 'EXECUTION_ASSET_OVERLAP', detail: 'secret' }, 409)));
    panel(); await confirm(user);
    expect(screen.getByRole('alert').textContent).toContain('EXECUTION_ASSET_OVERLAP');
    expect(screen.getByRole('alert').textContent).not.toContain('secret'); expect(sessionStorage.getItem(storageKey)).toBeTruthy();
  });
  it('clears and disables result recovery after denied readback', async () => {
    const user = userEvent.setup(); vi.stubGlobal('fetch', vi.fn(async (_url: string, init?: RequestInit) =>
      init?.method === 'POST' ? response(completed) : response({}, 403)));
    panel(); await confirm(user);
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', expect.stringContaining('Execution access is unavailable'));
    expect(screen.getByRole('button', { name: 'Recover saved Shadow request' })).toHaveProperty('disabled', true);
    expect(screen.queryByText('Completed')).toBeNull();
  });
  it('rejects a receipt for another revision without selecting an execution', async () => {
    const user = userEvent.setup(); const http = vi.fn(async () => response({ ...completed, configurationRevisionId: userId }));
    vi.stubGlobal('fetch', http); panel(); await confirm(user);
    expect(screen.getByRole('alert').textContent).toContain('outcome could not be verified'); expect(http).toHaveBeenCalledTimes(1);
  });
  it('aborts a closing panel without publishing a stale result', async () => {
    const user = userEvent.setup(); let resolve!: (r: Response) => void; let signal: AbortSignal | null | undefined;
    vi.stubGlobal('fetch', vi.fn((_url: string, init?: RequestInit) => { signal = init?.signal; return new Promise<Response>(r => { resolve = r; }); }));
    const view = panel(); await confirm(user); view.unmount(); expect(signal?.aborted).toBe(true);
    await act(async () => resolve(response(completed)));
    expect(sessionStorage.getItem(storageKey)).toBeTruthy();
  });
  it('offers no submission controls to a reader', () => {
    panel(false); expect(screen.queryByRole('button', { name: 'Review Shadow run' })).toBeNull();
    expect(screen.getByText(/do not allow requesting execution/)).toBeTruthy();
  });
  it('polls durable pending status through Running to Completed, and only then offers a new request', async () => {
    const user = userEvent.setup(); let reads = 0;
    vi.stubGlobal('fetch', vi.fn(async (_url: string, init?: RequestInit) => {
      if (init?.method === 'POST') return response({ ...completed, status: 'Requested' });
      reads++;
      return response({ ...completed, status: reads < 3 ? 'Running' : 'Completed' });
    }));
    panel(); await confirm(user); await screen.findByText('Running');
    expect(screen.queryByRole('button', { name: 'Prepare another Shadow run' })).toBeNull();
    await waitFor(() => expect(screen.getByText('Completed')).toBeTruthy(), { timeout: 4000 });
    await user.click(screen.getByRole('button', { name: 'Prepare another Shadow run' }));
    expect(sessionStorage.getItem(storageKey)).toBeNull();
    expect(screen.getByRole('button', { name: 'Review Shadow run' })).toBeTruthy();
  });
  it('renders bounded input quality and provenance as escaped text with locale-aware values', () => {
    render(<ExecutionDetailContent detail={{ ...completed, outputJson: JSON.stringify({ inputObservations: [{
      requirementId: 'fillage', assetId: userId, quantity: 'PumpFillage', value: 82.5, unit: '%',
      timestampUtc: '2026-10-16T12:00:00Z', quality: 'Good', source: '<script>bad()</script>'
    }] }) }} />);
    expect(screen.getByRole('region', { name: 'Input observations (up to 100)' })).toBeTruthy();
    expect(screen.getByText('82.5 %')).toBeTruthy(); expect(document.querySelector('script')).toBeNull();
    expect(screen.getByText('<script>bad()</script>')).toBeTruthy();
  });
});
