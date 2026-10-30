import { X } from 'lucide-react';
import { ActionButton } from './ActionButton';
import type { ActionButtonProps } from './ActionButton';
import { t } from '../localization';

/**
 * Closes a details pane or panel. Always the same ✕ icon button (with a tooltip and an accessible name), at the top
 * right of the pane, beside any Refresh icon: closing is a universally understood symbol, and an icon keeps the
 * pane header to one quiet row. Pass `label` when the name should say what closes ("Close details").
 */
export function CloseButton({ label, ...props }: Omit<ActionButtonProps, 'icon' | 'iconOnly' | 'label'> & { label?: string }) {
  return <ActionButton {...props} icon={X} iconOnly label={label ?? t('action.close')} />;
}
