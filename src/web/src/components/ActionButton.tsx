import type { ComponentProps } from 'react';
import type { LucideIcon } from 'lucide-react';
import './loading.css';
import './searchControls.css';

export type ActionButtonProps = Omit<ComponentProps<'button'>, 'children'> & {
  label: string;
  icon?: LucideIcon;
  iconOnly?: boolean;
  busy?: boolean;
  variant?: 'primary' | 'secondary';
  controlSize?: 'standard' | 'large';
};

/** Native activation and a stable accessible name for both icon and text actions. */
export function ActionButton({ label, icon: Icon, iconOnly = false, busy = false,
  variant = 'secondary', controlSize = 'standard', disabled, type = 'button',
  className = '', title, ...props }: ActionButtonProps) {
  const hideText = iconOnly && Boolean(Icon);
  return <button {...props} type={type} disabled={disabled || busy} aria-label={label}
    aria-busy={busy || undefined} title={title ?? (hideText ? label : undefined)}
    className={`ui-action-button ${variant}-button ${className}`}
    data-icon-only={hideText || undefined} data-size={controlSize}>
    {busy ? <span className="loading-spinner" aria-hidden="true" />
      : Icon ? <Icon size={16} aria-hidden="true" /> : null}
    <span className={hideText ? 'visually-hidden' : undefined}>{label}</span>
  </button>;
}
