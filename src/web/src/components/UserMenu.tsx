import { hasPermission } from '../security/sessionAccess';
import type { CurrentUserResponse } from '../api/generated/v1/types.gen';
import { t } from '../localization';

export function UserMenu({
  user,
  isLoading,
}: {
  user: CurrentUserResponse | null;
  isLoading: boolean;
}) {
  const displayName = user?.displayName?.trim() || user?.identityName ||
    (isLoading ? t('session.loading') : t('session.unavailable'));

  return (
    <details className="user-menu">
      <summary aria-label={t('session.label', { name: displayName })}>
        <span className="user-avatar" aria-hidden="true">
          {displayName.charAt(0).toUpperCase()}
        </span>
        <span className="user-label">{displayName}</span>
        <span className="user-menu-chevron" aria-hidden="true">⌃</span>
      </summary>
      <div className="user-panel">
        <strong>{t('session.heading')}</strong>
        {!user ? (
          <p role="status">{t('session.noDetails')}</p>
        ) : (
          <dl>
            <dt>{t('session.identity')}</dt>
            <dd>{user.identityName}</dd>
            <dt>{t('session.account')}</dt>
            <dd>{user.isActive ? t('session.active') : t('session.inactive')}</dd>
            <dt>{t('session.access')}</dt>
            <dd>{t('session.permissionCount', { count: user.permissions?.length ?? 0 })}</dd>
          </dl>
        )}
        <p className="user-panel-note">{t('session.authNotice')}</p>
        {hasPermission(user, 'security.manage') && (
          <a className="user-panel-link" href="#access">{t('access.menu')}</a>
        )}
      </div>
    </details>
  );
}
