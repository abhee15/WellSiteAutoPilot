import './loading.css';

/**
 * The one way a page or panel says it is loading, announced politely to assistive technology: a spinner and a
 * sentence, or (`variant="skeleton"`) pale placeholder rows where a list will appear. It stays invisible for a
 * moment (CSS delay) so a fast response never flashes it, and stops moving for people who ask for less motion.
 */
export function LoadingIndicator({
  label, size = 'medium', inline = false, variant = 'spinner',
}: {
  label: string;
  size?: 'small' | 'medium' | 'large';
  /** Sit in a line of text or a table cell rather than taking a block of its own. */
  inline?: boolean;
  /** Placeholder rows for a list or panel; the label stays for assistive technology only. */
  variant?: 'spinner' | 'skeleton';
}) {
  if (variant === 'skeleton') {
    return (
      <div role="status" aria-live="polite" className="loading-skeleton">
        <span className="visually-hidden">{label}</span>
        {[0, 1, 2, 3].map(row => <span key={row} className="loading-skeleton-row" aria-hidden="true" />)}
      </div>
    );
  }
  return (
    <div role="status" aria-live="polite" className="loading-indicator" data-size={size} data-inline={inline || undefined}>
      <span className="loading-spinner" aria-hidden="true" />
      <span className="loading-label">{label}</span>
    </div>
  );
}
