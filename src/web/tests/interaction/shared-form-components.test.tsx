import { createRef } from 'react';
import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { CheckboxField, ConfirmPanel, NumberField, RadioGroup, SelectField, TextAreaField, TextField } from '../../src/components/FormFields';
import { Notice } from '../../src/components/Notice';
import { accessibilityViolations } from './axe';

afterEach(cleanup);

it('connects the label, required state, external description and changing helper/error to the same native field', () => {
  const ref = createRef<HTMLInputElement>();
  const { rerender } = render(<><p id="context">UTC schedule</p><TextField ref={ref} label="Start date" type="date" required aria-describedby="context" helper="Choose a date" /></>);
  const field = screen.getByLabelText(/^Start date/) as HTMLInputElement;
  expect(ref.current).toBe(field);
  expect(field.type).toBe('date');
  expect(field.required).toBe(true);
  expect(field.id).not.toBe('');
  const id = field.id;
  expect(field.getAttribute('aria-describedby')).toBe(`context ${id}-message`);
  expect(document.getElementById(`${id}-message`)?.textContent).toBe('Choose a date');
  rerender(<><p id="context">UTC schedule</p><TextField label="Start date" type="date" required aria-describedby="context" helper="Choose a date" error="Start date must precede the end date" /></>);
  expect(screen.getByLabelText(/^Start date/).id).toBe(id);
  expect(field.getAttribute('aria-invalid')).toBe('true');
  expect(document.getElementById(`${id}-message`)?.textContent).toBe('Start date must precede the end date');
  expect(screen.queryByText('Choose a date')).toBeNull();
});

it('keeps repeated controls uniquely labelled and preserves native values, limits, read-only and disabled semantics', async () => {
  const onChange = vi.fn();
  const { container } = render(<>
    <TextField label="Source" value="ForeSite" readOnly />
    <NumberField label="Frequency" min={1} max={12} step={1} defaultValue={2} />
    <TextAreaField label="Reason" rows={3} maxLength={1024} disabled value="Review" onChange={onChange} />
    <SelectField label="Unit" defaultValue="hours"><option value="hours">Hours</option></SelectField>
    <TextField label="Source" defaultValue="CygNet" />
  </>);
  const ids = [...container.querySelectorAll('input, textarea, select')].map(field => field.id);
  expect(new Set(ids).size).toBe(ids.length);
  expect(screen.getAllByLabelText('Source')[0]).toHaveProperty('readOnly', true);
  expect(screen.getByRole('spinbutton')).toHaveProperty('min', '1');
  expect(screen.getByRole('spinbutton')).toHaveProperty('value', '2');
  expect(screen.getByRole('combobox')).toHaveProperty('value', 'hours');
  await userEvent.setup().type(screen.getByLabelText('Reason'), 'changed');
  expect(onChange).not.toHaveBeenCalled();
  expect(screen.getByLabelText('Reason')).toHaveProperty('value', 'Review');
  expect(await accessibilityViolations(container)).toEqual([]);
});

it('uses native radio activation, unique group names and disabled acknowledgement without preselecting', async () => {
  const onChange = vi.fn();
  render(<><RadioGroup label="Decision" value={null} required onChange={onChange}
    options={[{ value: 'approve', label: 'Approve' }, { value: 'reject', label: 'Reject' }]} />
    <RadioGroup label="Other decision" value={null} onChange={vi.fn()} options={[{ value: 'keep', label: 'Keep' }]} />
    <CheckboxField label="I checked the proposal" checked={false} disabled onChange={vi.fn()} /></>);
  const approve = screen.getByRole('radio', { name: 'Approve' }) as HTMLInputElement;
  const reject = screen.getByRole('radio', { name: 'Reject' }) as HTMLInputElement;
  expect(approve.checked).toBe(false);
  expect(approve.name).toBe(reject.name);
  expect(approve.name).not.toBe((screen.getByRole('radio', { name: 'Keep' }) as HTMLInputElement).name);
  await userEvent.setup().click(reject);
  expect(document.activeElement).toBe(reject);
  expect(onChange).toHaveBeenCalledWith('reject');
  expect(screen.getByRole('checkbox')).toHaveProperty('disabled', true);
});

it('announces outcomes once, keeps static guidance a note, and provides a named dismiss action', async () => {
  const dismiss = vi.fn();
  const { container } = render(<><Notice tone="warning" announce={false}>Approval does not send a command.</Notice>
    <Notice tone="error">The request was refused.</Notice>
    <Notice tone="success" onDismiss={dismiss}>Saved.</Notice></>);
  expect(screen.getAllByRole('alert')).toHaveLength(1);
  expect(screen.getAllByRole('status')).toHaveLength(1);
  expect(screen.getByRole('note').textContent).toContain('Approval does not send');
  await userEvent.setup().click(screen.getByRole('button', { name: 'Dismiss notice' }));
  expect(dismiss).toHaveBeenCalledOnce();
  expect(await accessibilityViolations(container)).toEqual([]);
});

it('confirmation stays in-page and leaves unrelated navigation available', async () => {
  render(<><a href="#assets">Assets</a><ConfirmPanel title="Confirm command execution"><CheckboxField label="I reviewed the command" /></ConfirmPanel></>);
  expect(screen.queryByRole('dialog')).toBeNull();
  expect(screen.getByRole('region', { name: 'Confirm command execution' })).toBeTruthy();
  await userEvent.setup().click(screen.getByRole('checkbox'));
  expect(screen.getByRole('checkbox')).toHaveProperty('checked', true);
  expect(screen.getByRole('link', { name: 'Assets' }).getAttribute('href')).toBe('#assets');
});
