import type { ReactNode } from 'react';
import './dataTable.css';

/**
 * The one table. `list` is the full-width record table that sits in a panel (panel gutters, uppercase column heads,
 * row hover); `embedded` is a small fact table inside running content such as a disclosure. The scroll wrapper and the
 * accessible name belong here so no page repeats them or forgets one.
 */
export function DataTable({ label, variant = 'list', className, children }: {
  /** The table's accessible name, announced before its first row. */
  label: string;
  variant?: 'list' | 'embedded';
  /** Page-owned layout hooks only (column hiding, row selection); never base table styling. */
  className?: string;
  children: ReactNode;
}) {
  const base = variant === 'embedded' ? 'data-table-embedded' : 'activity-table';
  return <div className="table-wrap">
    <table className={className ? `${base} ${className}` : base} aria-label={label}>{children}</table>
  </div>;
}
