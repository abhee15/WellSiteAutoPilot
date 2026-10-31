import { useCallback, useEffect, useState } from 'react';
import type { ControlActionResponse, KillSwitchResponse } from '../api/generated/v1/types.gen';
import { ActionButton } from '../components/ActionButton';
import { t } from '../localization';
import { useChangeRefresh, usePollingRefresh } from '../live/LiveUpdates';
import { formatDetailTimestamp } from './executionDetail';

/** Read-only governed-control audit plus the kill switch. Commands are only ever sent to the simulator. */
export function AutonomousControlPanel({ canManage }: { canManage: boolean }) {
  const [killSwitch, setKillSwitch] = useState<KillSwitchResponse | null>(null);
  const [actions, setActions] = useState<ControlActionResponse[]>([]);
  const [error, setError] = useState(false);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async (signal?: AbortSignal) => {
    try {
      const [ks, list] = await Promise.all([
        fetch('/api/v1/control/kill-switch', { signal, credentials: 'same-origin', cache: 'no-store' }),
        fetch('/api/v1/control/actions?limit=20', { signal, credentials: 'same-origin', cache: 'no-store' }),
      ]);
      if (!ks.ok || !list.ok) throw new Error('control');
      const state = await ks.json() as KillSwitchResponse;
      const rows = await list.json() as unknown;
      if (typeof state?.engaged !== 'boolean' || !Array.isArray(rows)) throw new Error('control');
      setKillSwitch(state);
      setActions(rows as ControlActionResponse[]);
      setError(false);
    } catch {
      if (!signal?.aborted) setError(true);
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  // The gate and the kill switch name the control area; polling is only the safety net.
  useChangeRefresh(['control'], () => void load());
  usePollingRefresh(() => void load());

  async function toggle() {
    if (!killSwitch || busy) return;
    setBusy(true);
    try {
      const response = await fetch('/api/v1/control/kill-switch', {
        method: 'PUT', credentials: 'same-origin', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ engaged: !killSwitch.engaged, reason: null }),
      });
      if (!response.ok) throw new Error('control');
      setKillSwitch(await response.json() as KillSwitchResponse);
      setError(false);
    } catch { setError(true); } finally { setBusy(false); }
  }

  return <section className="panel" aria-label={t('control.heading')}>
    <div className="panel-header"><h2>{t('control.heading')}</h2></div>
    <p className="recommendation-readonly-note">{t('control.note')}</p>
    {error && <p role="alert" className="execution-detail-error">{t('control.unavailable')}</p>}
    {killSwitch && <p role="status">
      {killSwitch.engaged ? t('control.killEngaged') : t('control.killReleased')}{' '}
      {killSwitch.autonomousControlEnabled ? t('control.globalOn') : t('control.globalOff')}
    </p>}
    {killSwitch && canManage && <ActionButton busy={busy}
      label={killSwitch.engaged ? t('control.release') : t('control.engage')} onClick={() => void toggle()} />}
    <div className="table-wrap">
      <table className="activity-table">
        <thead><tr>
          <th scope="col">{t('control.status')}</th><th scope="col">{t('control.command')}</th>
          <th scope="col">{t('control.gate')}</th><th scope="col">{t('control.sink')}</th>
          <th scope="col">{t('control.when')}</th>
        </tr></thead>
        <tbody>
          {actions.map(a => <tr key={a.id}>
            <td>{a.status}</td>
            <td><strong>{a.command}</strong><small>{a.value ?? '—'} {a.unit ?? ''}</small></td>
            <td>{a.gateCode}</td><td>{a.sink}</td><td>{formatDetailTimestamp(a.createdAtUtc)}</td>
          </tr>)}
          {actions.length === 0 && <tr><td colSpan={5}>{t('control.none')}</td></tr>}
        </tbody>
      </table>
    </div>
  </section>;
}
