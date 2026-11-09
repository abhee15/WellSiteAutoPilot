import { afterEach, expect, it } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { CollapsibleSection, Section } from '../../src/components/Section';
import { accessibilityViolations } from './axe';

afterEach(cleanup);

it('shows a static section as a heading with its hint and content', () => {
  render(
    <Section title="Configuration name" hint="Pick a name people will recognise.">
      <input aria-label="Name" />
    </Section>,
  );
  expect(screen.getByRole('heading', { level: 4, name: 'Configuration name' })).toBeTruthy();
  expect(screen.getByText('Pick a name people will recognise.')).toBeTruthy();
  expect(screen.getByLabelText('Name')).toBeTruthy();
});

it('folds and unfolds and keeps the person’s choice when the page re-renders', async () => {
  const user = userEvent.setup();
  const { rerender } = render(
    <CollapsibleSection title="Settings" hint="Values the logic uses.">
      <p>Body text</p>
    </CollapsibleSection>,
  );
  const details = document.querySelector('details') as HTMLDetailsElement;
  expect(details.open).toBe(false);
  const summary = screen.getByText('Settings').closest('summary') as HTMLElement;
  await user.click(summary);
  expect(details.open).toBe(true);
  rerender(
    <CollapsibleSection title="Settings" hint="Values the logic uses.">
      <p>Body text changed</p>
    </CollapsibleSection>,
  );
  expect(details.open).toBe(true);
  await user.click(summary);
  expect(details.open).toBe(false);
});

it('starts open when asked, hides the decorative marker and has no accessibility violations', async () => {
  const { container } = render(
    <>
      <Section title="Choose the logic">
        <input aria-label="Module" />
      </Section>
      <CollapsibleSection title="Targets and overrides" meta={3} open>
        <p>Body text</p>
      </CollapsibleSection>
      <CollapsibleSection title="Technical details" quiet>
        <p>Id</p>
      </CollapsibleSection>
    </>,
  );
  const [first, second] = Array.from(container.querySelectorAll('details'));
  expect(first.open).toBe(true);
  expect(second.open).toBe(false);
  expect(container.querySelector('summary svg')?.getAttribute('aria-hidden')).toBe('true');
  expect(screen.getByText('Targets and overrides').closest('summary')?.textContent).toContain('3');
  expect(await accessibilityViolations(container)).toEqual([]);
});
