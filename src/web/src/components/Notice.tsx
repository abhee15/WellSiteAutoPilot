import { CircleCheck, CircleX, Info, TriangleAlert } from 'lucide-react';
import type { ReactNode } from 'react';
import { CloseButton } from './CloseButton';
import { t } from '../localization';
import './notice.css';

const icons = { info: Info, success: CircleCheck, warning: TriangleAlert, error: CircleX };
/** Static guidance is a note; changing outcomes are announced once at the operation's location. */
export function Notice({ tone = 'info', announce = true, children, onDismiss, dismissLabel }: {
  tone?: keyof typeof icons; announce?: boolean; children: ReactNode; onDismiss?: () => void; dismissLabel?: string;
}) {
  const Icon = icons[tone];
  return <div className="ui-notice" data-tone={tone} role={announce ? tone === 'error' ? 'alert' : 'status' : 'note'}
    aria-atomic={announce || undefined}>
    <Icon className="ui-notice-icon" size={16} aria-hidden="true" />
    <div className="ui-notice-content">{children}</div>
    {onDismiss && <CloseButton label={dismissLabel ?? t('notice.dismiss')} onClick={onDismiss} />}
  </div>;
}
