import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import { ModulePackageState } from '../../src/views/ModulePackageState';
import { getLogicModulePackageState } from '../../src/api/generated/v1/sdk.gen';

vi.mock('../../src/api/generated/v1/sdk.gen', () => ({ getLogicModulePackageState: vi.fn() }));
afterEach(() => { cleanup(); vi.clearAllMocks(); });

const module = { moduleId: 'sample.module', version: '1.0.0', isEnabled: true };
const observed = { isStored: true, publicationRecorded: false, objectAvailable: false, publicationPending: true,
  attemptCount: 2, lastFailureCode: 'MODULE_PACKAGE_PUBLICATION_FAILED', nextAttemptAtUtc: '2026-10-27T12:00:05Z',
  runtimeCompatible: true, workerHealthy: true, gatewayHealthy: true, preflightReady: false,
  compatibilityFailureCode: null, observedAtUtc: '2026-10-27T12:00:00Z' };
function respond(value: object, status = 200) {
  vi.mocked(getLogicModulePackageState).mockResolvedValueOnce({
    data: value, response: new Response(null, { status }),
  } as Awaited<ReturnType<typeof getLogicModulePackageState>>);
}

it('shows delivery, artifact and runtime separately, including bounded retry facts', async () => {
  respond(observed); render(<ModulePackageState module={module} />);
  expect(await screen.findByText('Package preflight is not ready for hosted Shadow.')).toBeTruthy();
  expect(screen.getByText('Publication pending · 2 failed attempts')).toBeTruthy();
  expect(screen.getByText(/MODULE_PACKAGE_PUBLICATION_FAILED/)).toBeTruthy();
  expect(screen.getByText('Exact artifact available')).toBeTruthy();
  expect(getLogicModulePackageState).toHaveBeenCalledWith(expect.objectContaining({
    path: { moduleId: 'sample.module', moduleVersion: '1.0.0' }, credentials: 'same-origin', cache: 'no-store',
  }));
});

it('reports a ready observation without claiming source authorization', async () => {
  respond({ ...observed, publicationRecorded: true, objectAvailable: true, publicationPending: false,
    preflightReady: true, lastFailureCode: null, nextAttemptAtUtc: null });
  render(<ModulePackageState module={module} />);
  expect(await screen.findByText('Observed package preflight is ready for Shadow.')).toBeTruthy();
  expect(screen.getByText(/These observations do not authorize a run or verify a selected source/)).toBeTruthy();
  expect(screen.queryByText(/Publication pending/)).toBeNull();
});

it('does not render provider detail or an affirmative observation after refusal', async () => {
  respond({ detail: 'SECRET' }, 403); render(<ModulePackageState module={module} />);
  expect(await screen.findByRole('alert')).toHaveProperty('textContent',
    'Readiness could not be verified. Refresh before relying on it.');
  expect(screen.queryByText(/SECRET/)).toBeNull();
  expect(screen.queryByText('Observed package preflight is ready for Shadow.')).toBeNull();
});
