import { SearchInput } from '../components/SearchInput';
import { useEffect, useId, useRef, useState } from 'react';
import { formatTimestamp } from '../format';
import { LoadingIndicator } from '../components/LoadingIndicator';
import { t } from '../localization';
import type { MessageKey } from '../localization';
const factLabels: Record<string, MessageKey> = { sourceName: 'sourceChanges.sourceName', wellType: 'sourceChanges.wellType',
  wellTypeId: 'sourceChanges.wellTypeId', deviceType: 'sourceChanges.deviceType', assetName: 'sourceChanges.assetName', assetId: 'sourceChanges.assetId' };
const factLabel = (key: string) => factLabels[key] ? t(factLabels[key]) : key;
import { checkSourceChanges, ConnectionsError, loadSourceChanges } from './connections';
import type { SourceChangePage, SourceCheck } from './connections';

export function ConnectionSourceChanges({ connectionId, connectionRevision, canCheck, disabled, onBusyChange }: {
  connectionId: string; connectionRevision: number; canCheck: boolean; disabled: boolean; onBusyChange: (busy: boolean) => void;
}) {
  const [page, setPage] = useState<SourceChangePage | null>(null);
  const [check, setCheck] = useState<SourceCheck | null>(null);
  const [query, setQuery] = useState('');
  const [appliedQuery, setAppliedQuery] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const request = useRef<AbortController | null>(null);
  useEffect(() => () => request.current?.abort(), []);
  async function run(offset: number, nextQuery: string, checkNow = false) {
    request.current?.abort();
    const controller = new AbortController(); request.current = controller;
    setBusy(true); onBusyChange(true); setError(null);
    try {
      if (checkNow) {
        const result = await checkSourceChanges(connectionId);
        if (controller.signal.aborted) return;
        setCheck(result);
      }
      const result = await loadSourceChanges(connectionId, offset, nextQuery, controller.signal);
      if (!controller.signal.aborted) { setPage(result); setCheck(result.latestCheck); setAppliedQuery(nextQuery); }
    } catch (caught) {
      if (controller.signal.aborted) return;
      if (caught instanceof ConnectionsError && caught.kind === 'accessDenied') { setPage(null); setCheck(null); }
      setError(caught instanceof ConnectionsError && caught.kind === 'accessDenied' ? t('sourceChanges.denied')
        : caught instanceof ConnectionsError && caught.detail ? caught.detail : t('sourceChanges.failed'));
    } finally {
      if (request.current === controller && !controller.signal.aborted) { setBusy(false); onBusyChange(false); }
    }
  }
  const blocked = disabled || busy;
  const searchId = useId();
  return <section className="connection-source-changes" aria-label={t('sourceChanges.heading')}>
    <h3>{t('sourceChanges.heading')}</h3>
    <p className="connection-help">{t('sourceChanges.diagnostic')}</p>
    <div className="connection-actions">
      <button type="button" className="secondary-button" disabled={blocked || !canCheck}
        onClick={() => void run(0, '', true)}>{t('sourceChanges.check')}</button>
      <button type="button" className="secondary-button" disabled={blocked}
        onClick={() => void run(0, '')}>{t('sourceChanges.review')}</button>
    </div>
    {busy && <LoadingIndicator label={t('sourceChanges.loading')} size="small" />}
    {error && <p role="alert" className="status-error">{error}</p>}
    {check && <div className="connection-source-summary" role="status">
      <p>{t('sourceChanges.latest', { status: check.status, time: formatTimestamp(check.completedAtUtc ?? check.startedAtUtc), account: check.actingAs ?? t('sourceChanges.unknown') })}</p>
      {check.connectionRevision !== connectionRevision && <p>{t('sourceChanges.stale')}</p>}
      <p>{t('sourceChanges.counts', { found: check.found, changed: check.changed, added: check.new, missing: check.notReturned, unknown: check.comparisonUnavailable })}</p>
      {check.unavailableFields === null ? <p>{t('sourceChanges.coverageUnknown')}</p>
        : check.unavailableFields.length > 0 && <p>{t('sourceChanges.unavailable', { fields: check.unavailableFields.map(factLabel).join(', ') })}</p>}
      <p>{t('sourceChanges.scope', { count: check.sourceScopeCount ?? t('sourceChanges.scopeUnknown') })}</p>
      {check.warnings.map((warning, i) => <p key={i}>{warning}</p>)}
    </div>}
    {page && <>
      <p>{t('sourceChanges.lastComplete', { time: page.lastSuccessfulCheck ? formatTimestamp(page.lastSuccessfulCheck.completedAtUtc!) : t('sourceChanges.never') })}</p>
      <p className="connection-help">{t('sourceChanges.retained')}</p>
      <div className="connection-actions">
        <div className="connection-source-search"><label htmlFor={searchId}>{t('sourceChanges.search')}</label><SearchInput id={searchId} value={query} maxLength={256} disabled={blocked} placeholder={t('sourceChanges.searchPlaceholder')}
          action={{ label: t('sourceChanges.search'), type: 'button', disabled: blocked, busy, onClick: () => void run(0, query) }} onChange={e => setQuery(e.target.value)}
          onKeyDown={event => {
            if (event.key === 'Enter') { event.preventDefault(); event.stopPropagation(); if (!blocked) void run(0, query); }
          }} /></div>
      </div>
      <p>{t('sourceChanges.results', { total: page.total })}</p>
      <ul className="source-change-list">{page.items.map(item => <li key={item.id}>
        <details><summary>{item.name} · {item.status} · {item.externalId}</summary>
          <p>{t('sourceChanges.seen', { first: formatTimestamp(item.firstSeenAtUtc), last: formatTimestamp(item.lastSeenAtUtc) })}</p>
          {item.status === 'NotReturned' && <p>{t('sourceChanges.notDeleted')}</p>}
          {item.status === 'New' && <p>{t('sourceChanges.newWell')}</p>}
          {item.evidence.differences.length > 0 && <div className="source-change-table"><table><thead><tr>
            <th>{t('sourceChanges.fact')}</th><th>{t('sourceChanges.accepted')}</th><th>{t('sourceChanges.observed')}</th>
          </tr></thead><tbody>{item.evidence.differences.map(diff => <tr key={diff.field}>
            <td>{factLabel(diff.field)}</td><td>{diff.accepted}</td><td>{diff.observed}</td>
          </tr>)}</tbody></table></div>}
          {item.evidence.unknownFields.length > 0 && <p>{t('sourceChanges.unavailable', { fields: item.evidence.unknownFields.map(factLabel).join(', ') })}</p>}
          {item.referencesTruncated && <p>{t('sourceChanges.referencesTruncated')}</p>}
          <h4>{t('sourceChanges.views')}</h4>
          <ul>{item.views.map(view => <li key={view.id}>{view.name}</li>)}</ul>
          <h4>{t('sourceChanges.configurations')}</h4>
          <p className="connection-help">{t('sourceChanges.visibleOnly')}</p>
          <ul>{item.configurations.map(ref => <li key={`${ref.revisionId}:${ref.kind}:${ref.reference}`}>
            {ref.name} · r{ref.revisionNumber} · {ref.status} · {ref.kind}: {ref.reference}
          </li>)}</ul>
        </details>
      </li>)}</ul>
      <div className="connection-actions">
        <button type="button" className="secondary-button" disabled={blocked || page.offset === 0}
          onClick={() => void run(Math.max(0, page.offset - 25), appliedQuery)}>{t('sourceChanges.previous')}</button>
        <button type="button" className="secondary-button" disabled={blocked || page.offset + page.items.length >= page.total}
          onClick={() => void run(page.offset + 25, appliedQuery)}>{t('sourceChanges.next')}</button>
      </div>
    </>}
  </section>;
}
