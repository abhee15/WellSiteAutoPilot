import { readFileSync } from 'node:fs';
import { fireEvent, render, screen, cleanup } from '@testing-library/react';
import { afterEach, expect, it } from 'vitest';
import { GuidedModuleContract } from '../../src/views/GuidedModuleContract';

afterEach(cleanup);

it('inspects the real structured-card calibration manifest without inventing calculation steps', () => {
  const manifestJson = readFileSync(
    new URL('../../../../modules/samples/structured-cards/manifest.json', import.meta.url),
    'utf8',
  );
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
  expect(screen.getByText(/No outputs are declared.*does not exclude recommendations/)).toBeTruthy();
});

it('does not render a malformed manifest as a trustworthy contract', () => {
  const { container } = render(<GuidedModuleContract manifestJson="{bad-json" />);
  expect(container.querySelector('.guided-authoring-contract')).toBeNull();
});
