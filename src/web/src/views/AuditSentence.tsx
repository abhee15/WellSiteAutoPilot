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

/** The new state of a state change as a coloured pill; the label always carries the meaning, never the colour alone. */
export function OutcomePill({ outcome }: { outcome: NonNullable<AuditSummary['outcome']> }) {
  return <span className="activity-pill" data-tone={outcome.tone}>{outcome.label}</span>;
}
