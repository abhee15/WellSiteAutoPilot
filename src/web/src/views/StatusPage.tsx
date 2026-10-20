import './connections.css';
import { useCallback, useEffect, useRef, useState } from 'react';
import { t } from '../localization';
import type { MessageKey } from '../localization';
import { formatTimestamp, toCssToken } from '../format';
import { connectionStatus, loadAdapters, loadConnections } from './connections';
import type { Adapter, Connection, ConnectionStatus } from './connections';

const statusLabels: Record<ConnectionStatus, MessageKey> = {
  disabled: 'connections.statusDisabled', notTested: 'connections.statusNotTested',
  outdated: 'connections.statusOutdated', Healthy: 'connections.outcomeHealthy',
  NeedsAttention: 'connections.outcomeNeedsAttention', Unreachable: 'connections.outcomeUnreachable',
};

type ServiceInfo = { components: { name: string; version: string; status: string }[] };

type Part<T> = { state: 'loading' } | { state: 'failed' } | { state: 'ready'; value: T };

async function loadServices(signal: AbortSignal): Promise<ServiceInfo> {
  const response = await fetch('/api/v1/system/info', { signal, credentials: 'same-origin', cache: 'no-store' });
  if (!response.ok) throw new Error('services');
  const body: unknown = await response.json();
  const info = body as Partial<ServiceInfo> | null;
  if (!info || !Array.isArray(info.components)) throw new Error('services');
  return info as ServiceInfo;
}

/** One place to see whether the platform and the systems it reaches are working. Each part loads on its own. */
export function StatusPage({ canSeeServices, canSeeConnections }: { canSeeServices: boolean; canSeeConnections: boolean }) {
  const [services, setServices] = useState<Part<ServiceInfo>>({ state: 'loading' });
  const [connections, setConnections] = useState<Part<{ items: Connection[]; adapters: Adapter[] }>>({ state: 'loading' });
  const request = useRef<AbortController | null>(null);

  const reload = useCallback(async () => {
    request.current?.abort();
    const abort = new AbortController();
    request.current = abort;
    const current = () => request.current === abort;
    const tasks: Promise<void>[] = [];
    if (canSeeServices) {
      tasks.push(loadServices(abort.signal).then(
        value => { if (current()) setServices({ state: 'ready', value }); },
        () => { if (current() && !abort.signal.aborted) setServices({ state: 'failed' }); }));
    }
    if (canSeeConnections) {
      tasks.push((async () => {
        try {
          const items = await loadConnections(abort.signal);
          const adapters = await loadAdapters(abort.signal).catch(() => [] as Adapter[]);
          if (current()) setConnections({ state: 'ready', value: { items, adapters } });
        } catch {
          if (current() && !abort.signal.aborted) setConnections({ state: 'failed' });
        }
      })());
    }
    await Promise.all(tasks);
  }, [canSeeServices, canSeeConnections]);

  useEffect(() => {
    void reload();
    const timer = window.setInterval(() => void reload(), 30000);
    return () => { window.clearInterval(timer); request.current?.abort(); };
  }, [reload]);

  return (
    <div className="status-page">
      <div className="status-actions">
        <button type="button" onClick={() => void reload()}>{t('status.refresh')}</button>
      </div>
      {canSeeServices && (
        <section aria-labelledby="status-services">
          <h2 id="status-services">{t('status.services')}</h2>
          {services.state === 'loading' && <div className="status-loading">{t('status.loading')}</div>}
          {services.state === 'failed' && <div className="status-error" role="alert">{t('status.servicesFailed')}</div>}
          {services.state === 'ready' && (
            <div className="health-list">
              {services.value.components.map(component => (
                <div className="health-row" key={component.name}>
                  <div>
                    <strong>{component.name}</strong>
                    <small>{t('status.version', { version: component.version })}</small>
                  </div>
                  <span className={`health-state health-${toCssToken(component.status)}`}>{component.status}</span>
                </div>
              ))}
            </div>
          )}
        </section>
      )}
      {canSeeConnections && (
        <section aria-labelledby="status-connections">
          <h2 id="status-connections">{t('status.connections')}</h2>
          {connections.state === 'loading' && <div className="status-loading">{t('status.loading')}</div>}
          {connections.state === 'failed' && <div className="status-error" role="alert">{t('status.connectionsFailed')}</div>}
          {connections.state === 'ready' && connections.value.items.length === 0 && <p>{t('status.noConnections')}</p>}
          {connections.state === 'ready' && connections.value.items.length > 0 && (
            <div className="health-list">
              {connections.value.items.map(item => {
                const status = connectionStatus(item);
                const system = connections.value.adapters.find(a => a.key === item.adapterKey)?.displayName ?? item.adapterKey;
                return (
                  <div className="health-row" key={item.id}>
                    <div>
                      <a href={`#connections/${item.id}`}><strong>{item.name}</strong></a>
                      <small>{system} · {item.lastTest
                        ? t('status.tested', { when: formatTimestamp(item.lastTest.testedAtUtc) })
                        : t('status.neverTested')}</small>
                    </div>
                    <span className={`status-pill connection-test-${status.toLowerCase()}`}>{t(statusLabels[status])}</span>
                  </div>
                );
              })}
            </div>
          )}
        </section>
      )}
    </div>
  );
}
