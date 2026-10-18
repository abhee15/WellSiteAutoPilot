import { afterEach, expect, it } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
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
  expect(screen.getByText('Started by its run schedule')).toBeTruthy();
  expect(screen.getByText(/sends nothing to the equipment/)).toBeTruthy();
});

it('falls back to the module id and no asset links when the lists have not loaded', () => {
  render(<ExecutionDetailContent detail={{ ...detail, logicInstanceId: 'gone' } as never} />);
  expect(screen.getByText(/Logic:\s*sample\.module/)).toBeTruthy();
  expect(screen.queryByRole('link', { name: 'North Well' })).toBeNull();
});
