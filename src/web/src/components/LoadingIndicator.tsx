import './loading.css';

/**
 * The one way a page or panel says it is loading: a spinner and a sentence, announced politely to assistive
 * technology. It stays invisible for a moment (CSS delay) so a fast response never flashes it, and stops
 * spinning for people who ask for less motion.
 */
export function LoadingIndicator({
  label, size = 'medium', inline = false,
}: {
  label: string;
  size?: 'small' | 'medium' | 'large';
  /** Sit in a line of text or a table cell rather than taking a block of its own. */
  inline?: boolean;
}) {
  return (
    <div role="status" aria-live="polite" className="loading-indicator" data-size={size} data-inline={inline || undefined}>
      <span className="loading-spinner" aria-hidden="true" />
      <span className="loading-label">{label}</span>
    </div>
  );
}
