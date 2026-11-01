import { useEffect, useState } from 'react';
import { t } from '../localization';
import { hashFor } from '../routing';
import { LoadingIndicator } from '../components/LoadingIndicator';
import { Status } from '../components/Status';
import { ConnectionsError, loadAdapterProfiles } from './connections';
import type { AdapterProfile } from './connections';

/** Read-only description of each adapter: what it reads, what a connection needs, its limits and where to start authoring. */
export function AdaptersPanel({ selectedKey, onSelect, canAuthor }: {
  selectedKey: string | null;
  onSelect: (key: string) => void;
  canAuthor: boolean;
}) {
  const [profiles, setProfiles] = useState<AdapterProfile[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const abort = new AbortController();
    loadAdapterProfiles(abort.signal).then(setProfiles, (caught: unknown) => {
      if (abort.signal.aborted) return;
      setError(caught instanceof ConnectionsError && caught.kind === 'gatewayUnavailable'
        ? t('adapters.gateway') : caught instanceof ConnectionsError && caught.kind === 'accessDenied'
          ? t('connections.denied') : t('adapters.loadFailed'));
    });
    return () => abort.abort();
  }, []);

  const selected = profiles?.find(item => item.key === selectedKey) ?? profiles?.[0] ?? null;

  return (
    <div className="connections-layout">
      <section className="panel" aria-label={t('adapters.listLabel')}>
        <div className="panel-header"><h2>{t('adapters.listLabel')}</h2></div>
        <p className="connection-help adapter-intro">{t('adapters.intro')}</p>
        {!profiles && !error && <LoadingIndicator label={t('adapters.loading')} />}
        {error && <p role="alert" className="status-error">{error}</p>}
        {profiles?.length === 0 && <p className="empty-state">{t('adapters.empty')}</p>}
        <ul className="connection-list">
          {profiles?.map(item => (
            <li key={item.key}>
              <button type="button" className="connection-row" aria-current={item.key === selected?.key}
                onClick={() => onSelect(item.key)}>
                <strong>{item.displayName}</strong>
                <small>{[item.canRead && t('adapters.reads'), item.canDiscover && t('adapters.lists')].filter(Boolean).join(' · ')}</small>
              </button>
            </li>
          ))}
        </ul>
      </section>
      <section className="panel" aria-label={selected?.displayName ?? t('adapters.listLabel')}>
        {selected ? <AdapterDetail profile={selected} canAuthor={canAuthor} />
          : profiles && profiles.length > 0 && <p className="empty-state">{t('adapters.pick')}</p>}
      </section>
    </div>
  );
}

function AdapterDetail({ profile, canAuthor }: { profile: AdapterProfile; canAuthor: boolean }) {
  const summary = profile.connections;
  return (
    <div className="adapter-detail">
      <div className="panel-header"><h2>{profile.displayName}</h2></div>
      <p>{profile.description}</p>
      <p className="connection-help">
        {profile.canWrite ? t('adapters.writes', { system: profile.displayName }) : t('adapters.readOnly', { system: profile.displayName })}
      </p>

      <section aria-labelledby="adapter-quantities">
        <h3 id="adapter-quantities">{t('adapters.quantities')}</h3>
        {profile.canRead ? <>
          {profile.resourceKind && <p className="connection-help">{t('adapters.objectKind', { kind: profile.resourceKind })}</p>}
          <div className="table-wrap">
            <table className="activity-table">
              <thead><tr>
                <th scope="col">{t('adapters.quantity')}</th><th scope="col">{t('adapters.label')}</th>
                <th scope="col">{t('adapters.type')}</th><th scope="col">{t('adapters.unit')}</th>
                <th scope="col">{t('adapters.timestamp')}</th>
              </tr></thead>
              <tbody>
                {profile.quantities.map(item => (
                  <tr key={item.name}>
                    <th scope="row"><code>{item.name}</code></th>
                    <td>{item.label}{item.needsMapping && <><br /><small>{t('adapters.needsMapping')}</small></>}</td>
                    <td>{item.valueKind === 'Structured' ? t('adapters.typeStructured', { schema: item.schemaId ?? '' }) : t('adapters.typeNumber')}</td>
                    <td>{item.unit ?? '—'}</td>
                    <td><small>{item.timestampMeaning}</small></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </> : <p className="connection-help">{t('adapters.noReads')}</p>}
      </section>

      <section aria-labelledby="adapter-capabilities">
        <h3 id="adapter-capabilities">{t('adapters.capabilities')}</h3>
        <ul className="adapter-facts">
          {profile.capabilities.map(item => (
            <li key={item.kind + item.name}>{item.name} <small>({item.kind}, {t('adapters.contractVersion', { version: item.contractVersion })})</small></li>
          ))}
        </ul>
      </section>

      <section aria-labelledby="adapter-settings">
        <h3 id="adapter-settings">{t('adapters.settings')}</h3>
        <p className="connection-help">{t('adapters.settingsVersion', { version: String(profile.settingsVersion) })}</p>
        {profile.settings.length === 0 ? <p className="connection-help">{t('adapters.noSettings')}</p> : (
          <dl className="adapter-settings">
            {profile.settings.map(field => (
              <div key={field.key}>
                <dt>{field.label} <small>({field.required ? t('adapters.required') : t('adapters.optional')})</small></dt>
                <dd>{field.helpText}</dd>
              </div>
            ))}
          </dl>
        )}
        {profile.groupingFields.length > 0 && <>
          <h4>{t('adapters.grouping')}</h4>
          <p>{profile.groupingFields.map(field => field.label).join(', ')}</p>
        </>}
      </section>

      <section aria-labelledby="adapter-limits">
        <h3 id="adapter-limits">{t('adapters.limits')}</h3>
        <dl className="adapter-settings">
          <div>
            <dt>{t('adapters.cadence')}</dt>
            <dd>{profile.minimumCadenceSeconds === null ? t('adapters.cadenceUnknown')
              : t('adapters.cadenceSeconds', { seconds: String(profile.minimumCadenceSeconds) })}</dd>
          </div>
          {profile.limits.map(item => <div key={item.key}><dt>{item.label}</dt><dd>{item.value}</dd></div>)}
        </dl>
      </section>

      <section aria-labelledby="adapter-connections">
        <h3 id="adapter-connections">{t('adapters.connections')}</h3>
        <p>{t('adapters.connectionCount', { total: String(summary.total), enabled: String(summary.enabled) })}</p>
        {summary.enabled > 0 && (
          <ul className="adapter-facts adapter-health">
            {summary.healthy > 0 && <li><Status value="Healthy" label={`${t('adapters.healthy')}: ${String(summary.healthy)}`} /></li>}
            {summary.needsAttention > 0 && <li><Status value="NeedsAttention" label={`${t('adapters.needsAttention')}: ${String(summary.needsAttention)}`} /></li>}
            {summary.unreachable > 0 && <li><Status value="Unreachable" label={`${t('adapters.unreachable')}: ${String(summary.unreachable)}`} /></li>}
            {summary.notChecked > 0 && <li><Status value="notTested" label={`${t('adapters.notChecked')}: ${String(summary.notChecked)}`} /></li>}
          </ul>
        )}
      </section>

      {profile.canRead && (
        <section aria-labelledby="adapter-examples">
          <h3 id="adapter-examples">{t('adapters.examples')}</h3>
          {profile.examples.length === 0 ? <p className="connection-help">{t('adapters.noExamples')}</p> : <>
            <p className="connection-help">{t('adapters.examplesHelp')}</p>
            <ul className="adapter-facts">
              {profile.examples.map(item => (
                <li key={item.starterId}><strong>{item.label}</strong>: {item.description} <small>({item.quantities.join(', ')})</small></li>
              ))}
            </ul>
            {canAuthor && <a href={hashFor('logic', 'authoring')}>{t('adapters.openAuthoring')}</a>}
          </>}
          <p className="connection-help">{t('adapters.manifestHint', { key: profile.key })}</p>
        </section>
      )}
    </div>
  );
}
