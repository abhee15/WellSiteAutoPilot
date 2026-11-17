import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { cwd } from 'node:process';
import { fireEvent, render, screen, cleanup } from '@testing-library/react';
import { afterEach, expect, it } from 'vitest';
import { GuidedModuleContract } from '../../src/views/GuidedModuleContract';

afterEach(cleanup);

it('inspects the real structured-card calibration manifest without inventing calculation steps', () => {
  const manifestJson = readFileSync(resolve(cwd(), '../../modules/samples/structured-cards/manifest.json'), 'utf8');
  render(<GuidedModuleContract manifestJson={manifestJson} />);

  fireEvent.click(screen.getByText('Declared data contract', { exact: false }));
  expect(screen.getByText(/2 inputs · 2 outputs/)).toBeTruthy();
  expect(screen.getByRole('button', { name: 'SurfaceCards' })).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Calibration' })).toBeTruthy();
  expect(screen.getByRole('button', { name: 'CalibratedCards' })).toBeTruthy();
  expect(screen.getByRole('button', { name: 'PeakLoad' })).toBeTruthy();
  expect(screen.getByText(/not a map of calculations inside code/i)).toBeTruthy();

  fireEvent.click(screen.getByRole('button', { name: 'SurfaceCards' }));
  expect(screen.getByText('foresite.surface-cards/v1')).toBeTruthy();
  expect(screen.getByText('16')).toBeTruthy();
  expect(screen.getByText(/position: Array of number/)).toBeTruthy();
  expect(screen.getByText(/load: Array of number/)).toBeTruthy();

  fireEvent.click(screen.getByRole('button', { name: 'Calibration' }));
  expect(screen.queryByText('foresite.surface-cards/v1')).toBeNull();
  expect(screen.getByText('sample.calibration/v1')).toBeTruthy();
  expect(screen.getByText(/loadScale: number/)).toBeTruthy();
  expect(screen.getByText(/loadOffset: number/)).toBeTruthy();
});

it('distinguishes undeclared outputs from an absence of module recommendations', () => {
  render(
    <GuidedModuleContract
      manifestJson={JSON.stringify({
        dataRequirements: [{ id: 'fillage', quantity: 'PumpFillage', access: 'Current' }],
      })}
    />,
  );
  fireEvent.click(screen.getByText('Declared data contract', { exact: false }));
  expect(screen.getByText(/No typed outputs are declared/)).toBeTruthy();
});

it('does not render a malformed manifest as a trustworthy contract', () => {
  const { container } = render(<GuidedModuleContract manifestJson="{bad-json" />);
  expect(container.querySelector('.guided-authoring-contract')).toBeNull();
});

it('shows the declared code execution boundary without inventing a calculation graph', () => {
  const manifestJson = readFileSync(resolve(cwd(), '../../modules/samples/structured-cards/manifest.json'), 'utf8');
  render(<GuidedModuleContract manifestJson={manifestJson} initiallyOpen showExecutionBoundary />);

  expect(screen.getByRole('region', { name: 'Module execution' })).toBeTruthy();
  expect(screen.getByText('DotNet runtime')).toBeTruthy();
  expect(screen.getByText('sample.card-calibration · 2.0.0')).toBeTruthy();
  expect(
    screen.getByText(/Input-to-output dependencies and intermediate values require recorded execution evidence/i),
  ).toBeTruthy();
  expect(screen.queryByText('Load × scale + offset')).toBeNull();
  expect(screen.getByRole('button', { name: 'SurfaceCards' })).toBeTruthy();
  expect(screen.getByRole('button', { name: 'CalibratedCards' })).toBeTruthy();
  fireEvent.click(screen.getByRole('button', { name: 'PeakLoad' }));
  expect(screen.getByText('sample.peak-load/v2')).toBeTruthy();
});

it('does not manufacture an implementation runtime or intermediate steps when absent', () => {
  render(
    <GuidedModuleContract
      manifestJson={JSON.stringify({
        dataRequirements: [{ id: 'reading', quantity: 'Reading' }],
        outputRequirements: [{ id: 'output', quantity: 'Result' }],
      })}
      initiallyOpen
      showExecutionBoundary
    />,
  );
  expect(screen.getByText('Runtime not declared')).toBeTruthy();
  expect(screen.getByText('Internal steps not recorded.')).toBeTruthy();
  expect(
    screen.getByText(/Input-to-output dependencies and intermediate values require recorded execution evidence/i),
  ).toBeTruthy();
});

it('leaves the ordinary guided contract uncluttered when execution overview is disabled', () => {
  render(
    <GuidedModuleContract
      manifestJson={JSON.stringify({
        dataRequirements: [{ id: 'reading', quantity: 'Reading' }],
        outputRequirements: [{ id: 'output', quantity: 'Result' }],
      })}
      initiallyOpen
    />,
  );
  expect(screen.queryByRole('region', { name: 'Module execution' })).toBeNull();
});
