import type { FormEvent, ReactNode } from 'react';

type FilterBarProps = {
  /** Accessible name of the whole group of filters. */
  label: string;
  children: ReactNode;
  /** Quiet line under the fields, such as "3 of 12 roles". */
  note?: ReactNode;
  /** Buttons that belong to the bar (Apply, Clear filters). */
  actions?: ReactNode;
  /** When given the bar is a form, so Enter applies the filters. */
  onSubmit?: () => void;
  className?: string;
};

/** The one layout every list's search and filters share: labelled fields in a row, then actions and a count. */
export function FilterBar({ label, children, note, actions, onSubmit, className = '' }: FilterBarProps) {
  const body = <>
    <div className="data-view-fields filter-bar-fields">{children}</div>
    {(actions || note) && <div className="filter-bar-footer">
      {actions && <div className="filter-bar-actions">{actions}</div>}
      {note && <p className="data-view-note">{note}</p>}
    </div>}
  </>;
  const classes = `data-view-controls filter-bar ${className}`.trim();
  if (!onSubmit) return <div className={classes} role="group" aria-label={label}>{body}</div>;
  return <form className={classes} aria-label={label}
    onSubmit={(event: FormEvent) => { event.preventDefault(); onSubmit(); }}>{body}</form>;
}

/** A label above one control; use for search boxes, selects and anything else inside a FilterBar. */
export function FilterField({ label, children }: { label: string; children: ReactNode }) {
  return <label><span>{label}</span>{children}</label>;
}
