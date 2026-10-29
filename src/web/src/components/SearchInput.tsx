import type { ComponentProps } from 'react';
import { Search } from 'lucide-react';
import { ActionButton } from './ActionButton';
import type { ActionButtonProps } from './ActionButton';
import './searchControls.css';

type SearchAction = Pick<ActionButtonProps, 'label' | 'iconOnly' | 'busy' | 'disabled' | 'onClick' | 'type' | 'variant'>;
export type SearchInputProps = Omit<ComponentProps<'input'>, 'type' | 'size'> & {
  action?: SearchAction;
  controlSize?: 'standard' | 'large';
};

/** Presentation only: callers retain labels, criteria, requests and form/Enter behavior. */
export function SearchInput({ action, controlSize = 'standard', className = '', disabled, ...props }: SearchInputProps) {
  return <span className="ui-search-input" data-size={controlSize} data-disabled={disabled || undefined}>
    {!action && <Search className="ui-search-decoration" size={16} aria-hidden="true" />}
    <input {...props} type="search" disabled={disabled} className={`ui-search-control ${className}`} />
    {action && <ActionButton {...action} icon={Search} iconOnly={action.iconOnly ?? true}
      disabled={disabled || action.disabled} variant={action.variant ?? 'primary'} controlSize={controlSize}
      className="ui-search-action" />}
  </span>;
}
