import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { OverviewPage } from '../../src/views/OverviewPage';
import { OperationsPage } from '../../src/views/OperationsPage';
import { RecommendationsPage } from '../../src/views/RecommendationsPage';
import { StatusPage } from '../../src/views/StatusPage';
import { ActivityPage } from '../../src/views/ActivityPage';
import { Status } from '../../src/components/Status';
import { ActionButton } from '../../src/components/ActionButton';
import { CopyButton } from '../../src/views/CopyButton';
import * as connections from '../../src/views/connections';
import * as audit from '../../src/views/auditActivity';
import { Copy } from 'lucide-react';
import { accessibilityViolations } from './axe';

vi.mock('../../src/views/connections', async importOriginal => ({
  ...await importOriginal<typeof import('../../src/views/connections')>(),
  loadConnections: vi.fn(), loadAdapters: vi.fn(),
}));
vi.mock('../../src/views/auditActivity', async importOriginal => ({
  ...await importOriginal<typeof import('../../src/views/auditActivity')>(),
  loadAuditPage: vi.fn(),
}));

const json = (value: unknown) => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } });
beforeEach(() => {
  vi.mocked(connections.loadConnections).mockResolvedValue([]);
  vi.mocked(connections.loadAdapters).mockResolvedValue([]);
});
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

describe('accessibility (names, roles, labels and structure; contrast is checked in the browser)', () => {
  it('the check itself fails on a button with no name and an input with no label', async () => {
    const { container } = render(<><button type="button" /><input type="text" /></>);
    const found = await accessibilityViolations(container);
    expect(found.some(line => line.startsWith('button-name'))).toBe(true);
    expect(found.some(line => line.startsWith('label'))).toBe(true);
  });

  it('shared building blocks are clean', async () => {
    const { container } = render(<>
      <Status value="Failed" /><Status value="Completed" /><Status value="Unknown thing" />
      <ActionButton icon={Copy} iconOnly label="Copy record ID" />
      <ActionButton icon={Copy} iconOnly variant="quiet" label="Copy event ID" state="success" />
      <ActionButton label="Review" />
      <CopyButton value="abc" label="Record ID" />
    </>);
    expect(await accessibilityViolations(container)).toEqual([]);
  });

  it('Overview, with a failed run and the latest-runs list', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json([])));
    const { container } = render(<OverviewPage permissions={['executions.read', 'assets.read', 'configured-logic.read', 'recommendations.read']}
      systemHealth={null} isLoaded assets={[{ id: 'a1', name: 'Well 7-12H' }] as never} configuredLogic={[]}
      executions={[{ executionId: 'e1', assetId: 'a1', moduleId: 'esp', status: 'Failed',
        requestedAtUtc: new Date().toISOString() }] as never} />);
    await screen.findByText('Latest runs');
    expect(await accessibilityViolations(container)).toEqual([]);
  });

  it('Runs with a summary filter on, and with a filter that hides every row', async () => {
    const runs = [{ executionId: 'e1', status: 'Completed', mode: 'Shadow', moduleId: 'm', moduleVersion: '1',
      assetExternalId: 'Well 1', requestedAtUtc: '2026-10-28T03:00:00Z' }];
    const { container } = render(<OperationsPage executions={runs as never} userId="u1" />);
    expect(await accessibilityViolations(container)).toEqual([]);
    await userEvent.setup().click(screen.getByRole('button', { name: /Requires attention/ }));
    await screen.findByRole('button', { name: 'Clear filters' });
    expect(await accessibilityViolations(container)).toEqual([]);
  });

  it('Recommendations table', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json([{ id: 'r1', assetId: 'a1', status: 'Pending', canDecide: true,
      command: 'Review pump setting', reasonCode: 'pump_efficiency_low', createdAtUtc: new Date().toISOString(),
      expiresAtUtc: new Date(Date.now() + 3600_000).toISOString() }])));
    const { container } = render(<RecommendationsPage userId="11111111-1111-4111-8111-111111111111"
      permissions={['recommendations.read', 'recommendations.decide']} />);
    await screen.findByRole('button', { name: 'Review' });
    expect(await accessibilityViolations(container)).toEqual([]);
  });

  it('Status page', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json({ product: 'WellSiteAutoPilot', components: [
      { name: 'Server', version: '1', status: 'Healthy' }, { name: 'Gateway', version: '1', status: 'Degraded' }] })));
    const { container } = render(<StatusPage canSeeServices canSeeConnections />);
    await screen.findByText('Gateway');
    expect(await accessibilityViolations(container)).toEqual([]);
  });

  it('Activity list and an opened event', async () => {
    vi.mocked(audit.loadAuditPage).mockResolvedValue({ items: [{
      id: '00000000-0000-4000-8000-000000000001', occurredAtUtc: '2026-10-18T12:00:00Z', actorUserId: null,
      actorIdentity: 'system:connection-monitor', action: 'connection.status_changed', targetType: 'connection',
      targetId: '00000000-0000-4000-8000-000000000501', targetName: 'ForeSite', assetId: null, correlationId: null,
      detailsJson: '{"From":"Unreachable","To":"Healthy"}' }], nextCursor: null });
    const { container } = render(<ActivityPage />);
    (await screen.findByText('ForeSite')).closest('tr')?.querySelector('button')?.click();
    await screen.findByText('Event details');
    expect(await accessibilityViolations(container)).toEqual([]);
  });
});
