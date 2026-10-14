import { useMemo, useState } from 'react';
import type { ReactNode } from 'react';

export interface DataGridColumn<T> {
  id: string;
  header: string;
  render: (item: T) => ReactNode;
  searchValue?: (item: T) => string;
}

interface DataGridProps<T> {
  ariaLabel: string;
  rows: readonly T[];
  columns: readonly DataGridColumn<T>[];
  rowKey: (item: T) => string;
  emptyMessage: string;
}

export function DataGrid<T>({
  ariaLabel,
  rows,
  columns,
  rowKey,
  emptyMessage,
}: DataGridProps<T>) {
  const [query, setQuery] = useState('');
  const [sortColumn, setSortColumn] = useState<string | null>(null);
  const [descending, setDescending] = useState(false);
  const visibleRows = useMemo(() => {
    const normalizedQuery = query.trim().toLocaleLowerCase();
    const filtered = normalizedQuery
      ? rows.filter(item => columns.some(column =>
          (column.searchValue?.(item) ?? '').toLocaleLowerCase().includes(normalizedQuery)))
      : [...rows];

    const column = columns.find(item => item.id === sortColumn);
    if (column?.searchValue) {
      filtered.sort((left, right) => {
        const comparison = column.searchValue!(left).localeCompare(
          column.searchValue!(right), undefined, { numeric: true, sensitivity: 'base' });
        return descending ? -comparison : comparison;
      });
    }
    return filtered;
  }, [rows, columns, query, sortColumn, descending]);

  return (
    <>
      <div className="grid-toolbar">
        <label>
          <span>Filter {ariaLabel}</span>
          <input
            type="search"
            value={query}
            onChange={event => setQuery(event.target.value)}
            placeholder="Search visible records"
          />
        </label>
        <span aria-live="polite">{visibleRows.length} of {rows.length} shown</span>
      </div>
      <div className="table-wrap">
        <table className="activity-table" aria-label={ariaLabel}>
          <thead>
            <tr>
              {columns.map(column => (
                <th key={column.id} scope="col" aria-sort={
                  sortColumn === column.id ? (descending ? 'descending' : 'ascending') : 'none'
                }>
                  {column.searchValue ? (
                    <button className="grid-sort" type="button" onClick={() => {
                      if (sortColumn === column.id) setDescending(value => !value);
                      else { setSortColumn(column.id); setDescending(false); }
                    }}>{column.header}{sortColumn === column.id ? (descending ? ' ↓' : ' ↑') : ''}</button>
                  ) : column.header}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {visibleRows.map(item => (
              <tr key={rowKey(item)}>
                {columns.map(column => <td key={column.id}>{column.render(item)}</td>)}
              </tr>
            ))}
            {visibleRows.length === 0 && (
              <tr><td className="empty-state" colSpan={columns.length}>
                {rows.length ? 'No records match the filter.' : emptyMessage}
              </td></tr>
            )}
          </tbody>
        </table>
      </div>
    </>
  );
}
