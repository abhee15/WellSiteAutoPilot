import type { ReactNode } from 'react';
import { LoadingIndicator } from './LoadingIndicator';
import { Check, ChevronRight, Filter } from 'lucide-react';
import { StatusIndicator } from './Status';
import type { StatusTone } from './Status';
import './summaryCard.css';

type SummaryCardProps = {
  label: string;
  value: string;
  detail: string;
  tone?: StatusTone;
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
  tone,
}: SummaryCardProps) {
  const body = (
    <>
      <span className="summary-card-heading"><span>{label}</span>
        {href ? <ChevronRight className="summary-card-cue" size={16} aria-hidden="true" />
          : onSelect ? (selected ? <Check className="summary-card-cue" size={16} aria-hidden="true" />
            : <Filter className="summary-card-cue" size={16} aria-hidden="true" />) : null}
      </span>{' '}
      <strong>{tone ? <StatusIndicator tone={tone} label={value} /> : value}</strong>{' '}
      {detail && <small>{detail}</small>}
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

/** A panel title and its available actions share one responsive layout.
 *  Use level=3 only when a nested detail/editor already has a parent heading. */
export function PanelHeader({
  title,
  count,
  actions,
  level = 2,
}: {
  title: string;
  count?: number;
  actions?: ReactNode;
  level?: 2 | 3;
}) {
  const Heading = level === 3 ? 'h3' : 'h2';
  return (
    <div className="panel-header">
      <Heading>{title}</Heading>
      {count !== undefined && <span className="record-count">{count} shown</span>}
      {actions && <div className="panel-header-actions">{actions}</div>}
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
