import type { ReactNode } from 'react';
import { ChevronRight } from 'lucide-react';
import './section.css';

interface SectionProps {
  title: ReactNode;
  /** One quiet line under the title that says what the section is for. */
  hint?: ReactNode;
  children?: ReactNode;
}

/**
 * A titled part of a form or detail view. Every section in a form uses this, so they share one heading size,
 * one spacing rhythm and one divider, whether or not they can be collapsed.
 */
export function Section({ title, hint, children }: SectionProps) {
  return (
    <div className="ui-section">
      <h4 className="ui-section-title">{title}</h4>
      {hint && <p className="ui-section-hint">{hint}</p>}
      {children}
    </div>
  );
}

interface CollapsibleSectionProps extends SectionProps {
  /** Whether it starts open. Native details keeps the open state itself, so a person's choice survives re-renders. */
  open?: boolean;
  /** Short text at the end of the title row, such as a count. */
  meta?: ReactNode;
  /** A quiet section holds technical detail that most people never need; it reads as secondary. */
  quiet?: boolean;
  /** Extra class for the screen's own layout inside the section; never for the section's look. */
  className?: string;
}

/**
 * A section people can fold away. It is a native `<details>`, so keyboard, screen reader and find-in-page
 * behaviour come from the browser; only the marker is ours, so it matches the chevrons used elsewhere.
 */
export function CollapsibleSection({ title, hint, meta, open, quiet, className, children }: CollapsibleSectionProps) {
  return (
    <details
      className={['ui-section ui-section-collapsible', quiet && 'ui-section-quiet', className]
        .filter(Boolean)
        .join(' ')}
      open={open}
    >
      <summary className="ui-section-summary">
        <ChevronRight className="ui-section-marker" size={16} strokeWidth={2} aria-hidden="true" focusable="false" />
        <span className="ui-section-title">{title}</span>
        {meta && <span className="ui-section-meta">{meta}</span>}
      </summary>
      <div className="ui-section-body">
        {hint && <p className="ui-section-hint">{hint}</p>}
        {children}
      </div>
    </details>
  );
}
