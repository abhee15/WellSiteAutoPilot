import { afterEach, expect, it, vi } from 'vitest';
import { act, cleanup, render, screen, waitFor } from '@testing-library/react';
import { App } from '../../src/App';
import { LiveUpdatesProvider } from '../../src/live/LiveUpdates';
import { t } from '../../src/localization';

const userId = '99999999-9999-4999-8999-999999999999';

afterEach(() => {
  cleanup(); vi.useRealTimers(); vi.unstubAllGlobals(); vi.restoreAllMocks(); window.location.hash = ''; localStorage.clear();
});

/**
 * The first request to a Negotiate site can take longer than the 10 s poll (browser sign-in prompt, Kerberos or
 * NTLM round trips, the Server's first directory lookup). Aborting it to start another restarts the handshake
 * and raises another prompt, so a poll must wait for the load that is already running.
 */
it('does not abort or repeat a sign-in request that is still in flight when the poll fires', async () => {
  vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
  const started: { url: string; aborted: boolean; settle: (body: unknown) => void }[] = [];
  vi.stubGlobal('fetch', vi.fn((input: RequestInfo | URL, init?: RequestInit) => new Promise<Response>((resolve, reject) => {
    const entry = { url: String(input), aborted: false, settle: (body: unknown) => resolve(new Response(JSON.stringify(body), { status: 200 })) };
    started.push(entry);
    init?.signal?.addEventListener('abort', () => { entry.aborted = true; reject(new DOMException('aborted', 'AbortError')); });
  })));

  render(<LiveUpdatesProvider><App /></LiveUpdatesProvider>);
  await waitFor(() => expect(started.filter(r => r.url.endsWith('/security/me'))).toHaveLength(1));

  // While the session is being read the page says so, instead of staying blank.
  expect(screen.getByText(t('session.opening'))).toBeTruthy();

  await act(async () => { vi.advanceTimersByTime(10_000); });
  await act(async () => { vi.advanceTimersByTime(10_000); });

  const sessions = started.filter(r => r.url.endsWith('/security/me'));
  expect(sessions).toHaveLength(1);
  expect(sessions[0]!.aborted).toBe(false);

  await act(async () => { sessions[0]!.settle({ userId, identityName: 'DOMAIN\\user', isActive: true, permissions: [], displayName: null }); });
  await waitFor(() => expect(started.length).toBeGreaterThan(1)); // the load carries on once the session answers
});
