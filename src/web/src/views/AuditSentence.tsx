import type { AuditSummary } from './auditPresentation';

/** An event's sentence with the name of the affected record emphasised. */
export function AuditSentence({ summary }: { summary: AuditSummary }) {
  return (
    <>
      {summary.parts.map((part, index) => part.strong
        ? <strong key={index}>{part.text}</strong>
        : <span key={index}>{part.text}</span>)}
    </>
  );
}
