import { afterEach, expect, it } from 'vitest';
import { cleanup, render, screen, within } from '@testing-library/react';
import { ExecutionDetailContent } from '../../src/views/ExecutionDetailPanel';

afterEach(cleanup);

const configuration = { id: 'cfg-1', name: 'Pressure watch', revisions: [
  { id: 'rev-2', revisionNumber: 2, assetBindings: [{ assetId: 'asset-1' }] }] };
const assets = [{ id: 'asset-1', name: 'North Well' }];
const detail = { executionId: 'run-1', logicInstanceId: 'cfg-1', configurationRevisionId: 'rev-2',
  status: 'Completed', mode: 'Shadow', trigger: 'Scheduled', resultCode: 'RecommendationProduced',
  moduleId: 'sample.module', moduleVersion: '1.0.0' };

it('says in words what the run did and links to its logic and assets', () => {
  render(<ExecutionDetailContent detail={detail as never} configuredLogic={[configuration] as never} assets={assets as never} />);
  expect(screen.getByText('Produced a recommendation')).toBeTruthy();
  expect(screen.getByRole('link', { name: 'Pressure watch' }).getAttribute('href')).toBe('#logic/cfg-1');
  expect(screen.getByRole('link', { name: 'North Well' }).getAttribute('href')).toBe('#assets/asset-1');
  expect(screen.getByText(/Revision 2/)).toBeTruthy();
  expect(screen.getByText('Scheduled')).toBeTruthy();
  expect(screen.queryByText(/Started by/)).toBeNull();
  expect(screen.getByText(/sends nothing to the equipment/)).toBeTruthy();
});

it('falls back to the module id and no asset links when the lists have not loaded', () => {
  render(<ExecutionDetailContent detail={{ ...detail, logicInstanceId: 'gone' } as never} />);
  expect(screen.getByText('sample.module')).toBeTruthy();
  expect(screen.queryByRole('link', { name: 'North Well' })).toBeNull();
});

const failedRun = {
  executionId: '11111111-1111-4111-8111-111111111111', logicInstanceId: 'cfg-1', configurationRevisionId: 'rev-2',
  status: 'Failed', mode: 'Shadow', trigger: 'Manual', failureCode: 'EXECUTION_INPUT_TIMESTAMP_UNVERIFIED',
  moduleId: 'sample.module', moduleVersion: '1.0.0', correlationId: 'corr-9',
  requestedAtUtc: '2026-10-28T03:33:27Z', startedAtUtc: '2026-10-28T03:33:28Z', completedAtUtc: '2026-10-28T03:33:30Z',
  outputJson: JSON.stringify({ inputDiagnostics: [{ requirementId: 'pump-fillage', assetId: 'asset-1',
    connectionId: '22222222-2222-4222-8222-222222222222', quantity: 'PumpFillage', value: 99.6, unit: '%',
    timestampUtc: '2026-10-28T02:51:35Z', retrievedAtUtc: '2026-10-28T03:33:29Z', timestampKind: 'UnverifiedScan',
    quality: 'Unknown', outcomeCode: 'EXECUTION_INPUT_TIMESTAMP_UNVERIFIED' }] }),
};

it('names what happened, what each input was and where it came from, in words', () => {
  render(<ExecutionDetailContent detail={failedRun as never} configuredLogic={[configuration] as never}
    assets={assets as never}
    connectionNames={new Map([['22222222-2222-4222-8222-222222222222', 'North field gateway']])} />);
  expect(screen.getAllByText('An input’s measurement time could not be verified').length).toBeGreaterThan(0);
  expect(screen.getByText('Manual')).toBeTruthy();
  expect(screen.getByText(/Duration: 2 s/)).toBeTruthy();
  const inputs = screen.getByRole('region', { name: 'Inputs this run read' });
  expect(within(inputs).getByText('Pump fillage')).toBeTruthy();
  expect(within(inputs).getByText('North Well')).toBeTruthy();
  expect(within(inputs).getByText('North field gateway')).toBeTruthy();
  expect(within(inputs).getByText('99.6 %')).toBeTruthy();
  expect(within(inputs).getByText(/old when read/)).toBeTruthy();
  expect(within(inputs).queryByText(/22222222/)).toBeNull();
});

it('falls back to a short connection id when the name is not available', () => {
  render(<ExecutionDetailContent detail={failedRun as never} assets={assets as never} />);
  expect(screen.getByText(/Saved connection/)).toBeTruthy();
});

it('keeps identifiers and raw output in a collapsed technical section with copy buttons', () => {
  render(<ExecutionDetailContent detail={failedRun as never} />);
  const technical = document.querySelector('details.run-technical') as HTMLDetailsElement;
  expect(technical.open).toBe(false);
  expect(within(technical).getByText('Correlation ID')).toBeTruthy();
  expect(within(technical).getByRole('button', { name: 'Copy Execution ID' })).toBeTruthy();
  expect(within(technical).getByText('Raw output')).toBeTruthy();
  expect(screen.queryByText(/Read-only execution information/)).toBeNull();
});

it('shows a result as labelled rows instead of raw JSON', () => {
  render(<ExecutionDetailContent detail={{ ...detail, outputJson: JSON.stringify({
    recommendedFrequency: 52.5, note: 'Within range', inputDiagnostics: [] }) } as never} />);
  const result = screen.getByRole('region', { name: 'Result' });
  expect(within(result).getByText('Recommended frequency')).toBeTruthy();
  expect(within(result).getByText('52.5')).toBeTruthy();
  expect(within(result).getByText('Within range')).toBeTruthy();
  expect(within(result).queryByText(/inputDiagnostics|Input diagnostics/)).toBeNull();
});
