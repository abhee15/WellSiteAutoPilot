import type { ReactNode } from 'react';
import { LoadingIndicator } from './LoadingIndicator';

type SummaryCardProps = {
  label: string;
  value: string;
  detail: string;
} & (
  | { href: string; onSelect?: never; selected?: never }
  | { href?: never; onSelect?: () => void; selected?: boolean }
);

/** A metric: a link to another page, a filter button within this page, or a read-only stat. */
export function SummaryCard({
  label,
  value,
  detail,
  href,
  onSelect,
  selected = false,
}: SummaryCardProps) {
  const body = (
    <>
      <span>{label}</span>
      <strong>{value}</strong>
      <small>{detail}</small>
    </>
  );
  if (href) return <a className="summary-card summary-card-link" href={href}>{body}</a>;
  if (!onSelect) return <article className="summary-card">{body}</article>;
  return (
    <button type="button" className="summary-card summary-card-action" aria-pressed={selected} onClick={onSelect}>
      {body}
    </button>
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
  action,
  loading = false,
}: {
  columns: number;
  children: string;
  hint?: string;
  /** What to do about it, such as clearing the filters that hid every row. */
  action?: ReactNode;
  /** The text says the rows are still loading, so show it with the loading indicator. */
  loading?: boolean;
}) {
  return (
    <tr>
      <td className="empty-state" colSpan={columns}>
        {loading ? <LoadingIndicator label={children} /> : children}
        {hint && <small className="empty-hint">{hint}</small>}
        {action && <div className="empty-action">{action}</div>}
      </td>
    </tr>
  );
}
