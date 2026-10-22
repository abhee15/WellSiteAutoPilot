import { toCssToken } from '../format';
import { statusLabel } from '../words';
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
  eyebrow,
  title,
  count,
}: {
  eyebrow: string;
  title: string;
  count?: number;
}) {
  return (
    <div className="panel-header">
      <div>
        <p className="eyebrow">{eyebrow}</p>
        <h2>{title}</h2>
      </div>
      {count !== undefined && <span className="record-count">{count} shown</span>}
    </div>
  );
}

export function StatusPill({ value, label }: { value?: string | null; label?: string }) {
  return (
    <span className={`status-pill status-${toCssToken(value)}`}>
      {label ?? statusLabel(value)}
    </span>
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
