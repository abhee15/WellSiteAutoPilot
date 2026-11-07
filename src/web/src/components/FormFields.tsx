import { useId } from 'react';
import type { ComponentProps, ReactNode } from 'react';
import { t } from '../localization';
import './forms.css';

type Description = { label: string; helper?: string; error?: string };
type NativeInput = Omit<ComponentProps<'input'>, 'children' | 'className' | 'style' | 'type' | 'size'>;
type FieldState = { id: string; 'aria-describedby'?: string; 'aria-invalid'?: ComponentProps<'input'>['aria-invalid'] };
const describedBy = (...ids: (string | undefined)[]) => ids.filter(Boolean).join(' ') || undefined;

function FieldShell({ label, helper, error, id: suppliedId, required, description, invalid, children }: Description & {
  id?: string; required?: boolean; description?: string; invalid?: FieldState['aria-invalid']; children: (state: FieldState) => ReactNode;
}) {
  const generatedId = useId();
  const id = suppliedId ?? generatedId;
  const message = error || helper;
  return <div className="ui-field" data-invalid={Boolean(error) || undefined}>
    <label className="ui-field-label" htmlFor={id}>{label}{required && <span className="ui-field-required" aria-hidden="true"> {t('form.required')}</span>}</label>
    {children({ id, 'aria-describedby': describedBy(description, message ? id + '-message' : undefined), 'aria-invalid': error ? true : invalid })}
    {message && <p className="ui-field-message" data-error={Boolean(error) || undefined} id={id + '-message'}>{message}</p>}
  </div>;
}

export function TextField({ label, helper, error, id, required, type = 'text', 'aria-describedby': description, 'aria-invalid': invalid, ...props }: NativeInput & Description & {
  type?: 'text' | 'url' | 'email' | 'password' | 'tel' | 'date' | 'time' | 'datetime-local';
}) {
  return <FieldShell {...{ label, helper, error, id, required, description, invalid }}>{state =>
    <input {...props} {...state} type={type} required={required} className="ui-field-control" />}</FieldShell>;
}

export function NumberField({ label, helper, error, id, required, 'aria-describedby': description, 'aria-invalid': invalid, ...props }: NativeInput & Description) {
  return <FieldShell {...{ label, helper, error, id, required, description, invalid }}>{state =>
    <input {...props} {...state} type="number" required={required} className="ui-field-control" />}</FieldShell>;
}

export function TextAreaField({ label, helper, error, id, required, 'aria-describedby': description, 'aria-invalid': invalid, ...props }: Omit<ComponentProps<'textarea'>, 'children' | 'className' | 'style'> & Description) {
  return <FieldShell {...{ label, helper, error, id, required, description, invalid }}>{state =>
    <textarea {...props} {...state} required={required} className="ui-field-control" />}</FieldShell>;
}

export function SelectField({ label, helper, error, id, required, 'aria-describedby': description, 'aria-invalid': invalid, children, ...props }: Omit<ComponentProps<'select'>, 'className' | 'style' | 'multiple' | 'size'> & Description) {
  return <FieldShell {...{ label, helper, error, id, required, description, invalid }}>{state =>
    <select {...props} {...state} required={required} className="ui-field-control">{children}</select>}</FieldShell>;
}

export function CheckboxField({ label, helper, error, id: suppliedId, required, 'aria-describedby': description, 'aria-invalid': invalid, ...props }: Omit<NativeInput, 'readOnly'> & Description) {
  const generatedId = useId();
  const id = suppliedId ?? generatedId;
  const message = error || helper;
  return <div className="ui-field" data-invalid={Boolean(error) || undefined}>
    <label className="ui-choice" htmlFor={id}>
      <input {...props} id={id} type="checkbox" required={required} className="ui-choice-control"
        aria-invalid={error ? true : invalid} aria-describedby={describedBy(description, message ? id + '-message' : undefined)} />
      <span>{label}{required && <span className="ui-field-required" aria-hidden="true"> {t('form.required')}</span>}</span>
    </label>
    {message && <p className="ui-field-message" data-error={Boolean(error) || undefined} id={id + '-message'}>{message}</p>}
  </div>;
}

export function RadioGroup({ label, helper, error, id: suppliedId, name, required, disabled, value, options, onChange }: Description & {
  id?: string; name?: string; required?: boolean; disabled?: boolean; value: string | null;
  options: readonly { value: string; label: string; disabled?: boolean }[]; onChange: (value: string) => void;
}) {
  const generatedId = useId();
  const id = suppliedId ?? generatedId;
  const message = error || helper;
  return <fieldset className="ui-radio-group" disabled={disabled} aria-invalid={error ? true : undefined}
    aria-describedby={message ? id + '-message' : undefined}>
    <legend>{label}{required && <span className="ui-field-required" aria-hidden="true"> {t('form.required')}</span>}</legend>
    {options.map((option, index) => <label className="ui-choice" key={option.value} htmlFor={`${id}-${index}`}>
      <input id={`${id}-${index}`} className="ui-choice-control" type="radio" name={name ?? id} required={required}
        value={option.value} checked={value === option.value} disabled={option.disabled}
        onChange={() => onChange(option.value)} />
      <span>{option.label}</span>
    </label>)}
    {message && <p className="ui-field-message" data-error={Boolean(error) || undefined} id={id + '-message'}>{message}</p>}
  </fieldset>;
}

/**
 * One row of form buttons: Cancel or Discard first, the primary action last. A destructive action goes in `leading`,
 * which sits apart at the start of the row so it is never next to the primary action.
 */
export function FormActions({ children, leading }: { children: ReactNode; leading?: ReactNode }) {
  return <div className="ui-form-actions">
    {leading && <span className="ui-form-actions-leading">{leading}</span>}
    {children}
  </div>;
}

/** Existing consequential controls stay in-page; this component adds no dialog or extra workflow step. */
export function ConfirmPanel({ title, level = 4, children }: {
  title: string;
  /** Heading level under the surrounding panel's heading (h2 panel: 3; nested editor under an h3: 4) so levels never skip. */
  level?: 3 | 4;
  children: ReactNode;
}) {
  const id = useId();
  const Heading = level === 3 ? 'h3' : 'h4';
  return <section className="ui-confirm-panel" aria-labelledby={id}><Heading id={id}>{title}</Heading>{children}</section>;
}
