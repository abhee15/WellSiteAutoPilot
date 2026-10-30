import { LoadingIndicator } from './LoadingIndicator';

export function SummaryCard({
  label,
  value,
  detail,
}: {
  label: string;
  value: string;
  detail: string;
}) {
  return (
    <article className="summary-card">
      <span>{label}</span>
      <strong>{value}</strong>
      <small>{detail}</small>
    </article>
  );
}

export function PanelHeader({
  title,
  count,
}: {
  title: string;
  count?: number;
}) {
  return (
    <div className="panel-header">
      <h2>{title}</h2>
      {count !== undefined && <span className="record-count">{count} shown</span>}
    </div>
  );
}

/** An empty table body. The optional hint says what the person can do about it. */
export function EmptyRow({
  columns,
  children,
  hint,
  loading = false,
}: {
  columns: number;
  children: string;
  hint?: string;
  /** The text says the rows are still loading, so show it with the loading indicator. */
  loading?: boolean;
}) {
  return (
    <tr>
      <td className="empty-state" colSpan={columns}>
        {loading ? <LoadingIndicator label={children} /> : children}
        {hint && <small className="empty-hint">{hint}</small>}
      </td>
    </tr>
  );
}
