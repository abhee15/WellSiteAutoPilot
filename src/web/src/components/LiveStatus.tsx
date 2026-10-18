import { t } from '../localization';
import { useLiveUpdates } from '../live/LiveUpdates';

/**
 * Connection state of the live-update channel. The wording carries the meaning; the dot is only
 * decoration. Nothing is shown until live updates are asked for, so a deployment or test without
 * the channel looks exactly as it did before.
 */
export function LiveStatus() {
  const { state, pollIntervalMs } = useLiveUpdates();
  if (state === 'idle') return null;
  const seconds = Math.round(pollIntervalMs / 1000);
  const label = state === 'connected' ? t('live.connected')
    : state === 'connecting' ? t('live.connecting')
      : state === 'reconnecting' ? t('live.reconnecting')
        : t('live.polling', { seconds });
  const detail = state === 'connected' ? t('live.connectedDetail', { seconds })
    : state === 'disconnected' ? t('live.pollingDetail', { seconds }) : undefined;
  return (
    <span className="live-status" data-state={state} role="status" title={detail}>
      <span className="live-status-dot" aria-hidden="true" />
      {label}
    </span>
  );
}
