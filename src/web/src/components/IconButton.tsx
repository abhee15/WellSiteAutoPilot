import type { LucideIcon } from 'lucide-react';

/**
 * A button that is only an icon, for quiet actions (copy, reload). The label is required: it is the accessible
 * name and the hover tip. Every icon-only action uses this, so they all look, size and behave alike.
 */
export function IconButton({
  icon: Icon, label, onClick, disabled = false, state, className = '', title,
}: {
  icon: LucideIcon;
  label: string;
  onClick: () => void;
  disabled?: boolean;
  /** Briefly shows an outcome in colour (for example a copied check); the icon itself should change too. */
  state?: 'success' | 'error';
  className?: string;
  title?: string;
}) {
  return (
    <button type="button" className={`icon-button ${className}`.trim()} aria-label={label} title={title ?? label}
      data-state={state} disabled={disabled} onClick={onClick}>
      <Icon size={16} strokeWidth={1.75} aria-hidden="true" focusable="false" />
    </button>
  );
}
