import { useEffect, useRef } from 'react';
import { t } from '../localization';
import { formatDetailTimestamp } from './executionDetail';
import type { OperatorAlert } from './operatorAlerts';

type Props = {
  alerts: readonly OperatorAlert[];
  onClose: () => void;
};

export function OperatorAlertsPanel({ alerts, onClose }: Props) {
  const closeButton = useRef<HTMLButtonElement | null>(null);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose();
    };
    document.addEventListener('keydown', onKeyDown);
    closeButton.current?.focus();
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [onClose]);

  return (
    <aside className="operator-alerts-panel"
      role="complementary" aria-label={t('alerts.heading')}>
      <header className="operator-alerts-header">
        <div>
          <h2>{t('alerts.heading')}</h2>
          <p>{t('alerts.sourceNotice')}</p>
        </div>
        <button ref={closeButton} type="button" onClick={onClose}
          aria-label={t('alerts.close')}>
          {t('alerts.close')}
        </button>
      </header>
      <p className="operator-alerts-disclaimer">
        {t('alerts.disclaimer')}
      </p>
      <div className="operator-alerts-list">
        {alerts.map(alert => (
          <article className="operator-alert-item" key={alert.id}>
            <span className="operator-alert-kind">
              {alert.category === 'runtime' ? t('alerts.runtime')
                : t('alerts.execution')}
              {' · '}
              {alert.kind === 'unavailable'
                ? t('alerts.unavailable') : t('alerts.attention')}
            </span>
            <strong>{alert.label}</strong>
            <p>{alert.detail}</p>
            {alert.occurredAtUtc && (
              <small>{formatDetailTimestamp(alert.occurredAtUtc)}</small>
            )}
          </article>
        ))}
        {alerts.length === 0 && (
          <p className="status-loading">{t('alerts.none')}</p>
        )}
      </div>
      <p className="operator-alerts-footer">{t('alerts.loadedLimit')}</p>
    </aside>
  );
}
