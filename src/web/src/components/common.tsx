import { toCssToken } from '../format';

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

export function StatusPill({ value }: { value?: string | null }) {
  return (
    <span className={`status-pill status-${toCssToken(value)}`}>
      {value ?? 'Unknown'}
    </span>
  );
}

export function EmptyRow({
  columns,
  children,
}: {
  columns: number;
  children: string;
}) {
  return (
    <tr>
      <td className="empty-state" colSpan={columns}>{children}</td>
    </tr>
  );
}
