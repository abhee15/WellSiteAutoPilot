import { useEffect, useId, useRef } from 'react';
import { t } from '../localization';
import './multiSelect.css';

export type MultiSelectOption<T extends string> = { value: T; label: string };

/**
 * A dropdown of checkboxes. Nothing ticked means "no filter" and shows the `allLabel`; one tick shows
 * that option's name; more show how many are ticked. It closes on Escape or a click elsewhere.
 */
export function MultiSelect<T extends string>({
  label, options, selected, onChange, allLabel,
}: {
  label: string;
  options: readonly MultiSelectOption<T>[];
  selected: readonly T[];
  onChange: (next: T[]) => void;
  allLabel: string;
}) {
  const details = useRef<HTMLDetailsElement>(null);
  const labelId = useId();
  useEffect(() => {
    const close = (event: Event) => {
      const element = details.current;
      if (element?.open && event.target instanceof Node && !element.contains(event.target)) element.open = false;
    };
    const escape = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && details.current?.open) {
        details.current.open = false;
        details.current.querySelector('summary')?.focus();
      }
    };
    document.addEventListener('pointerdown', close);
    document.addEventListener('keydown', escape);
    return () => { document.removeEventListener('pointerdown', close); document.removeEventListener('keydown', escape); };
  }, []);
  const chosen = options.filter(option => selected.includes(option.value));
  const summary = chosen.length === 0 ? allLabel
    : chosen.length === 1 ? chosen[0].label : t('multiSelect.count', { count: String(chosen.length) });
  const toggle = (value: T) => onChange(selected.includes(value)
    ? selected.filter(item => item !== value)
    : options.map(option => option.value).filter(item => item === value || selected.includes(item)));
  return (
    <div className="multi-select">
      <span id={labelId}>{label}</span>
      <details ref={details}>
        <summary aria-labelledby={labelId}><span>{summary}</span></summary>
        <div className="multi-select-menu" role="group" aria-labelledby={labelId}>
          {options.map(option => (
            <label key={option.value}>
              <input type="checkbox" checked={selected.includes(option.value)} onChange={() => toggle(option.value)} />
              <span>{option.label}</span>
            </label>
          ))}
          <button type="button" className="multi-select-clear" disabled={selected.length === 0}
            onClick={() => onChange([])}>{t('multiSelect.clear')}</button>
        </div>
      </details>
    </div>
  );
}
