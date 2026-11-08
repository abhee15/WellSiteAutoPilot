import type { ReactNode } from 'react';

/**
 * Preformatted technical text (JSON, schemas, raw output). It can scroll sideways, and a scrolling area that cannot
 * take focus is unreachable by keyboard, so it is focusable and named. Styling stays with the surrounding context
 * (selectors such as `.activity-raw pre`), because the element is still a `pre`.
 */
export function CodeBlock({ label, className, children }: { label: string; className?: string; children: ReactNode }) {
  return <pre className={className} role="region" aria-label={label} tabIndex={0}>{children}</pre>;
}
