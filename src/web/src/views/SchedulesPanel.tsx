import { useCallback, useEffect, useState } from 'react';
import type { ScheduleResponse } from '../api/generated/v1/types.gen';
import { ActionButton } from '../components/ActionButton';
import { t } from '../localization';
import { useChangeRefresh, usePollingRefresh } from '../live/LiveUpdates';
import { formatDetailTimestamp } from './executionDetail';

type Props = {
  /** Holders of configured-logic.manage may edit; the server still decides per configuration. */
  canManage: boolean;
  /** Narrows the list to one configuration (client side) or to what runs on an asset or view (server side). */
  configuredLogicId?: string;
  assetId?: string;
  viewId?: string;
  heading?: string;
  hideWhenEmpty?: boolean;
};

function scheduleUrl(assetId?: string, viewId?: string) {
  const query = new URLSearchParams({ limit: '200' });
  if (assetId) query.set('assetId', assetId);
  if (viewId) query.set('viewId', viewId);
  return '/api/v1/schedules?' + query.toString();
}

async function problemCode(response: Response): Promise<string | undefined> {
  try {
    const problem = await response.json() as { code?: unknown };
    return typeof problem.code === 'string' && /^[A-Z][A-Z0-9_]{0,99}$/.test(problem.code) ? problem.code : undefined;
  } catch { return undefined; }
}

/**
 * Schedules across configurations. Read-only unless the user can manage; each edit is sent against the
 * active revision, so a stale view is refused instead of overwritten.
 */
export function SchedulesPanel({ canManage, configuredLogicId, assetId, viewId, heading, hideWhenEmpty = false }: Props) {
  const [rows, setRows] = useState<ScheduleResponse[] | null>(null);
  const [error, setError] = useState(false);
  const [editing, setEditing] = useState<string | null>(null);
  const [draft, setDraft] = useState('');
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const title = heading ?? t('schedules.heading');

  const load = useCallback(async (signal?: AbortSignal) => {
    try {
      const response = await fetch(scheduleUrl(assetId, viewId), { signal, credentials: 'same-origin', cache: 'no-store' });
      if (!response.ok) throw new Error('schedules');
      const body = await response.json() as unknown;
      if (!Array.isArray(body)) throw new Error('schedules');
      const all = body as ScheduleResponse[];
      setRows(configuredLogicId ? all.filter(r => r.configuredLogicId === configuredLogicId) : all);
      setError(false);
    } catch {
      if (!signal?.aborted) setError(true);
    }
  }, [assetId, viewId, configuredLogicId]);

  useEffect(() => {
    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  // A saved schedule or a finished scheduled run names these areas; polling is only the safety net.
  useChangeRefresh(['configured-logic', 'executions'], () => void load());
  usePollingRefresh(() => void load());

  async function send(row: ScheduleResponse, path: string, method: 'PUT' | 'POST', body?: unknown) {
    if (busy) return;
    setBusy(true); setMessage(null);
    try {
      const response = await fetch(`/api/v1/configured-logic/${row.configuredLogicId}/schedule${path}`, {
        method, credentials: 'same-origin',
        headers: { 'If-Match': `"${row.activeRevisionId}"`, ...(body ? { 'Content-Type': 'application/json' } : {}) },
        body: body ? JSON.stringify(body) : undefined,
      });
      if (!response.ok) {
        const code = await problemCode(response);
        if (code === 'CONFIGURED_LOGIC_SCHEDULE_BELOW_MINIMUM')
          setMessage(t('schedules.belowMinimum', { minimum: row.minimumCadenceSeconds ?? 15 }));
        else if (code === 'CONFIGURED_LOGIC_REVISION_CONFLICT') setMessage(t('schedules.conflict'));
        else setMessage(t('schedules.saveFailed') + (code ? ` (${code})` : ''));
        void load();
        return;
      }
      setEditing(null);
      await load();
    } catch { setMessage(t('schedules.saveFailed')); } finally { setBusy(false); }
  }

  function startEdit(row: ScheduleResponse) {
    setEditing(row.configuredLogicId ?? null); setDraft(String(row.cadenceSeconds ?? '')); setMessage(null);
  }

  function lastResult(row: ScheduleResponse) {
    const run = row.lastRun;
    if (!run?.status) return t('schedules.noRuns');
    return run.failureCode ?? run.resultCode ?? run.status;
  }

  if (hideWhenEmpty && !error && rows?.length === 0) return null;
  return <section className="panel" aria-label={title}>
    <div className="panel-header"><h2>{title}</h2></div>
    {!canManage && <p className="recommendation-readonly-note">{t('schedules.readOnly')}</p>}
    {error && <p className="execution-detail-error">{t('schedules.unavailable')}</p>}
    {message && <p role="alert" className="execution-detail-error">{message}</p>}
    <div className="table-wrap">
      <table className="activity-table">
        <thead><tr>
          <th scope="col">{t('schedules.configuration')}</th><th scope="col">{t('schedules.target')}</th>
          <th scope="col">{t('schedules.mode')}</th><th scope="col">{t('schedules.frequency')}</th>
          <th scope="col">{t('schedules.state')}</th><th scope="col">{t('schedules.lastRun')}</th>
          <th scope="col">{t('schedules.nextRun')}</th>
          {canManage && <th scope="col">{t('schedules.actions')}</th>}
        </tr></thead>
        <tbody>
          {(rows ?? []).map(row => {
            const mayEdit = canManage && row.canManage === true;
            const raised = (row.effectiveCadenceSeconds ?? 0) > (row.cadenceSeconds ?? 0);
            const isEditing = editing === row.configuredLogicId;
            return <tr key={row.configuredLogicId}>
              <td><a href={'#logic/' + row.configuredLogicId}>{row.name}</a></td>
              <td>{(row.targets ?? []).map(target => target.name).join(', ') || (row.viewId ? t('schedules.view') : '—')}</td>
              <td>{row.mode}</td>
              <td>
                {isEditing ? <input type="number" aria-label={t('schedules.cadenceInput')} min={row.minimumCadenceSeconds ?? 15}
                  value={draft} onChange={event => setDraft(event.target.value)} />
                  : <>{t('schedules.everySeconds', { seconds: row.cadenceSeconds ?? 0 })}
                    {raised && <small>{t('schedules.effective', { seconds: row.effectiveCadenceSeconds ?? 0 })}</small>}</>}
                <small>{t('schedules.minimum', { seconds: row.minimumCadenceSeconds ?? 15 })}</small>
              </td>
              <td>{row.enabled ? (row.running ? t('schedules.running') : t('schedules.active')) : t('schedules.paused')}</td>
              <td>{row.lastRun?.scheduledForUtc ? formatDetailTimestamp(row.lastRun.scheduledForUtc) : '—'}
                <small>{lastResult(row)}</small></td>
              <td>{row.enabled && row.nextRunAtUtc ? formatDetailTimestamp(row.nextRunAtUtc) : '—'}</td>
              {canManage && <td>{mayEdit ? (isEditing ? <>
                <ActionButton busy={busy} label={t('schedules.save')}
                  onClick={() => void send(row, '', 'PUT', { cadenceSeconds: Number(draft) })} />
                <ActionButton label={t('schedules.cancel')} onClick={() => setEditing(null)} />
              </> : <>
                <ActionButton label={t('schedules.edit')} onClick={() => startEdit(row)} />
                <ActionButton busy={busy} label={row.enabled ? t('schedules.pause') : t('schedules.resume')}
                  onClick={() => void send(row, row.enabled ? '/pause' : '/resume', 'POST')} />
              </>) : '—'}</td>}
            </tr>;
          })}
          {rows && rows.length === 0 && <tr><td colSpan={canManage ? 8 : 7}>{t('schedules.none')}</td></tr>}
        </tbody>
      </table>
    </div>
  </section>;
}
