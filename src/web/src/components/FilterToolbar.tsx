import { useEffect, useId, useRef, useState } from 'react';
import type { FormEvent, ReactNode } from 'react';
import { SlidersHorizontal, X } from 'lucide-react';
import { ActionButton } from './ActionButton';
import { SearchInput } from './SearchInput';
import { t } from '../localization';
import './filterToolbar.css';

export type FilterChip = { id: string; label: string; onRemove: () => void };

type FilterToolbarProps = {
  /** Accessible name of the whole toolbar. */
  label: string;
  /** The one search box. Everything else lives behind the Filters button. */
  search?: { value: string; onChange: (value: string) => void; placeholder?: string; label: string; maxLength?: number };
  /** Fields inside the Filters panel (selects, multi-selects, saved views). Omit for a search-only toolbar. */
  children?: ReactNode;
  /** One chip per applied filter shown in the panel; the count on the Filters button follows. */
  chips?: readonly FilterChip[];
  /** Whether anything (search included) narrows the list. Shows "Clear filters". */
  isFiltered?: boolean;
  onClear?: () => void;
  /** Quiet line beside the chips, such as "3 of 12 roles". */
  note?: ReactNode;
  /** Sits at the end of the first row (a Reload icon button, say). */
  trailing?: ReactNode;
  /** Makes the toolbar a form, so Enter applies the typed filters. */
  onSubmit?: () => void;
  className?: string;
};

/**
 * The one way every list is searched and filtered: a search box and a Filters button on a single row, the filter
 * fields in a panel that opens in place, and a chip for each applied filter so nothing is hidden. On narrow screens
 * the search takes the full row and the Filters panel stacks its fields.
 */
export function FilterToolbar({ label, search, children, chips = [], isFiltered = false, onClear, note, trailing, onSubmit,
  className = '' }: FilterToolbarProps) {
  const [open, setOpen] = useState(false);
  const panelId = useId();
  const root = useRef<HTMLElement | null>(null);
  useEffect(() => {
    if (!open) return;
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && root.current?.contains(document.activeElement)) setOpen(false);
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [open]);

  const body = <>
    <div className="filter-toolbar-row">
      {search && <div className="filter-toolbar-search">
        <SearchInput maxLength={search.maxLength ?? 120} value={search.value} aria-label={search.label}
          placeholder={search.placeholder ?? search.label} onChange={event => search.onChange(event.target.value)} />
      </div>}
      {children && <button type="button" className="filter-toolbar-toggle" aria-expanded={open} aria-controls={panelId}
        onClick={() => setOpen(value => !value)}>
        <SlidersHorizontal size={16} aria-hidden="true" />
        <span>{t('filters.toggle')}</span>
        {chips.length > 0 && <span className="filter-toolbar-badge" aria-label={t('filters.active', { count: chips.length })}>{chips.length}</span>}
      </button>}
      {trailing && <div className="filter-toolbar-trailing">{trailing}</div>}
    </div>
    {children && <div id={panelId} className="filter-toolbar-panel data-view-controls" hidden={!open}>{children}</div>}
    {(chips.length > 0 || isFiltered || note) && <div className="filter-toolbar-summary">
      {chips.map(chip => <span className="filter-chip" key={chip.id}>
        {chip.label}
        <button type="button" className="filter-chip-remove" aria-label={t('filters.remove', { name: chip.label })} title={t('filters.remove', { name: chip.label })}
          onClick={chip.onRemove}><X size={14} aria-hidden="true" /></button>
      </span>)}
      {isFiltered && onClear && <ActionButton variant="quiet" label={t('views.clearFilters')} onClick={onClear} />}
      {note && <p className="filter-toolbar-note">{note}</p>}
    </div>}
  </>;
  const classes = `filter-toolbar ${className}`.trim();
  if (!onSubmit) return <section className={classes} role="group" aria-label={label} ref={root as React.RefObject<HTMLElement>}>{body}</section>;
  return <form className={classes} aria-label={label} ref={root as React.RefObject<HTMLFormElement>}
    onSubmit={(event: FormEvent) => { event.preventDefault(); onSubmit(); }}>{body}</form>;
}

/** A label above one control inside the Filters panel. */
export function FilterField({ label, children }: { label: string; children: ReactNode }) {
  return <label><span>{label}</span>{children}</label>;
}
