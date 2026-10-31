import { ActionButton } from '../components/ActionButton';
import { RefreshCw } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import type { AssetResponse, AssetTypeResponse, ConfiguredLogicRevisionResponse, ExecutionResponse,
  LogicModuleResponse } from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { ConfiguredShadowPanel } from './ConfiguredShadowPanel';
import { ConfiguredLogicSetupPanel } from './ConfiguredLogicSetupPanel';
import { describeSettings } from './revisionSettings';
import { scheduleText } from './logicSummary';
import { orderedRevisions } from './configuredLogicViews';
import { formatDetailTimestamp } from './executionDetail';
import { ConfiguredLogicRevisionOverview } from './ConfiguredLogicRevisionOverview';
import { SchedulesPanel } from './SchedulesPanel';
import { readConfiguration, setupResponse, SetupApiError } from './configuredLogicSetup';
import type { VerifiedConfiguration } from './configuredLogicSetup';
import { LoadingIndicator } from '../components/LoadingIndicator';

type VerifiedRevision = VerifiedConfiguration['revisions'][number];
type Props = {
  selectedId: string | null; canManage: boolean; userId?: string | null; canRequestShadow?: boolean; modules: LogicModuleResponse[];
  assets: AssetResponse[]; assetTypes: AssetTypeResponse[]; executions?: readonly ExecutionResponse[];
  onClose: () => void; onChanged: () => Promise<void>;
  initialAssetId?: string | null; canReadViews?: boolean;
};
/** A fresh authorized detail is the mutation boundary. Polling list metadata
 * cannot silently replace the revision a user is reviewing. */
export function ConfiguredLogicWorkspace({ selectedId, canManage, userId, canRequestShadow, modules,
  assets, assetTypes, executions, onClose, onChanged, initialAssetId, canReadViews = false }: Props) {
  const [detail, setDetail] = useState<VerifiedConfiguration | null>(null);
  const [editing, setEditing] = useState(!selectedId);
  const [source, setSource] = useState<ConfiguredLogicRevisionResponse | undefined>();
  const [busy, setBusy] = useState(false);
  const [blocked, setBlocked] = useState(false);
  const [denied, setDenied] = useState(false);
  const [confirmId, setConfirmId] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const mayManage = canManage && (!selectedId || detail?.canManage === true);
  const request = useRef<AbortController | null>(null);
  const pending = useRef(false);
  useEffect(() => {
    if (!selectedId) return;
    const c = new AbortController(); request.current = c;
    void readConfiguration(selectedId, c.signal).then(value => {
      if (!c.signal.aborted) setDetail(value);
    }).catch(reason => {
      if (!c.signal.aborted) {
        setDenied(reason instanceof SetupApiError && reason.kind === 'denied');
        setError(t('setup.unavailable')); setBlocked(true);
      }
    });
    return () => c.abort();
  }, [selectedId]);
  useEffect(() => () => request.current?.abort(), []);

  async function reload() {
    if (pending.current || !detail && !selectedId) return;
    pending.current = true; setBusy(true);
    const c = new AbortController(); request.current?.abort(); request.current = c;
    try {
      const fresh = await readConfiguration(detail?.id ?? selectedId!, c.signal);
      if (c.signal.aborted) return;
      setDetail(fresh); setBlocked(false); setDenied(false); setError(null);
      setConfirmId(null); setNotice(t('setup.refreshed'));
    } catch (reason) {
      if (c.signal.aborted) return;
      if (reason instanceof SetupApiError && reason.kind === 'denied') {
        setDetail(null); setDenied(true); setEditing(false); setSource(undefined);
      }
      setBlocked(true); setError(t('setup.unavailable'));
    } finally { pending.current = false; if (!c.signal.aborted) setBusy(false); }
  }
  async function transition(revision: ConfiguredLogicRevisionResponse, action: 'validate' | 'activate') {
    if (!detail || !revision.id || !mayManage || busy || blocked || pending.current) return;
    const snapshot = detail;
    pending.current = true; setBusy(true); setError(null); setNotice(null);
    const c = new AbortController(); request.current?.abort(); request.current = c;
    let committed = false;
    try {
      const result = await setupResponse(await fetch('/api/v1/configured-logic/' + snapshot.id +
        '/revisions/' + revision.id + '/' + action, { method: 'POST', credentials: 'same-origin',
        signal: c.signal, headers: action === 'activate'
          ? { 'If-Match': '"' + (snapshot.activeRevisionId ?? 'none') + '"' } : {} }), 200, true);
      committed = true;
      const receipt = result as ConfiguredLogicRevisionResponse;
      if (receipt.id !== revision.id || receipt.status !== (action === 'activate' ? 'Active' : 'Validated'))
        throw new SetupApiError('uncertain');
      const fresh = await readConfiguration(snapshot.id, c.signal);
      const persisted = fresh.revisions.find(r => r.id === revision.id);
      if (persisted?.status !== receipt.status ||
        (action === 'activate' && fresh.activeRevisionId !== revision.id))
        throw new SetupApiError('conflict');
      if (c.signal.aborted) return;
      setDetail(fresh); setConfirmId(null); setNotice(t(action === 'activate' ? 'setup.activated' : 'setup.validated'));
      void onChanged();
    } catch (reason) {
      if (c.signal.aborted) return;
      const e = reason instanceof SetupApiError ? reason : new SetupApiError('uncertain');
      setBlocked(true); setConfirmId(null);
      if (e.kind === 'denied') { setDetail(null); setDenied(true); setSource(undefined); setEditing(false); }
      setError(t(committed ? 'setup.committed' : `setup.${e.kind}`) + (e.code ? ` (${e.code})` : ''));
    } finally { pending.current = false; if (!c.signal.aborted) setBusy(false); }
  }
  return <section className="setup-panel" aria-label={t('setup.workspace')}>
    {notice && <p role="status">{notice}</p>}
    {error && <p role="alert">{error}</p>}
    {!detail && selectedId && !error && <LoadingIndicator label={t('setup.loading')} />}
    {detail && <>
      <ConfiguredLogicRevisionOverview logic={detail} onClose={onClose} executions={executions} />
      {selectedId && !editing && <SchedulesPanel configuredLogicId={detail.id} canManage={mayManage} heading={t('schedules.forConfiguration')} />}
      <p>{t('setup.immutable')}</p>
      {(() => {
        const ordered = orderedRevisions(detail);
        const isLive = (r: VerifiedRevision) => r.id === detail.activeRevisionId;
        const waiting = (r: VerifiedRevision) => r.status === 'Draft' || r.status === 'Validated';
        const current = [...ordered.filter(isLive), ...ordered.filter(r => !isLive(r) && waiting(r))];
        const earlier = ordered.filter(r => !current.includes(r));
        const card = (r: VerifiedRevision) => <li key={r.id} data-live={isLive(r)}>
        <strong>{t('setup.revision', { number: r.revisionNumber ?? '—' })} · {r.status}</strong>
        {r.id === detail.activeRevisionId && <span className="revision-live">{t('configured.liveBadge')}</span>}
        <p className="revision-meta">{[
          [r.moduleId, r.moduleVersion && 'v' + r.moduleVersion].filter(Boolean).join(' '),
          r.mode,
          scheduleText(r.schedule),
          (r.assetBindings?.length ?? 0) === 1 ? t('configured.assetCountOne') : t('configured.assetCount', { count: r.assetBindings?.length ?? 0 }),
          (r.dataBindings?.length ?? 0) === 1 ? t('configured.sourcesOne') : t('configured.sources', { count: r.dataBindings?.length ?? 0 }),
          formatDetailTimestamp(r.createdAtUtc),
        ].filter(Boolean).join(' · ')}</p>
        <p>{t('setup.exactScope', { assets: r.assetBindings?.map(b =>
          assets.find(a => a.id === b.assetId)?.name ?? b.assetId).join(', ') ?? '' })}</p>
        <RevisionSettings revision={r} />
        {mayManage && !editing && <div className="setup-actions">
          <ActionButton label={t('setup.copyRevision', { number: r.revisionNumber ?? '—' })} disabled={busy || blocked} onClick={() => { setSource(r); setEditing(true); setConfirmId(null); }} />
          {r.status === 'Draft' && <ActionButton label={t('setup.validate', { number: r.revisionNumber ?? '—' })} disabled={busy || blocked} onClick={() => void transition(r, 'validate')} />}
          {r.status === 'Validated' && <ActionButton label={t('setup.reviewActivation', { number: r.revisionNumber ?? '—' })} disabled={busy || blocked} onClick={() => setConfirmId(r.id)} />}
          {confirmId === r.id && <div role="group" aria-label={t('setup.confirmHeading')}>
            <p>{t('setup.confirmText', { number: r.revisionNumber ?? '—' })}</p>
            <ActionButton label={t('setup.confirm')} disabled={busy || blocked} onClick={() => void transition(r, 'activate')} />
            <ActionButton label={t('setup.cancelActivation')} disabled={busy} onClick={() => setConfirmId(null)} />
          </div>}
        </div>}
        {userId && r.status === 'Active' && r.id === detail.activeRevisionId && r.mode !== 'Autonomous' && !editing && <ConfiguredShadowPanel
          key={r.id} userId={userId} configurationId={detail.id} revision={r}
          canRequest={canRequestShadow === true && detail.canRequestShadow === true} disabled={busy || blocked} />}
      </li>;
        return <>
          {current.length > 0 && <ul className="setup-revisions">{current.map(card)}</ul>}
          {earlier.length > 0 && <details className="revision-history" open={current.length === 0}>
            <summary>{t('configured.earlier', { count: earlier.length })}</summary>
            <ul className="setup-revisions">{earlier.map(card)}</ul>
          </details>}
        </>;
      })()}
    </>}
    {(detail || selectedId) && !editing && <ActionButton icon={RefreshCw} iconOnly label={t('setup.reload')} disabled={busy} onClick={() => void reload()} />}
    {editing && mayManage && !denied && (!selectedId || detail) && <ConfiguredLogicSetupPanel
      modules={modules} assets={assets} assetTypes={assetTypes} canReadViews={canReadViews}
      configuration={detail ?? undefined} revision={source} initialAssetId={initialAssetId}
      onSaved={value => { setDetail(value); setEditing(false); setSource(undefined);
        setNotice(t('setup.saved')); void onChanged(); }}
      onDenied={committed => { setDetail(null); setEditing(false); setSource(undefined);
        setDenied(true); setBlocked(true); setError(t(committed ? 'setup.committed' : 'setup.denied')); }}
      onCancel={() => { setEditing(false); setSource(undefined); if (!detail) onClose(); }} />}
    {!detail && <ActionButton label={t('configured.close')} disabled={busy} onClick={onClose} />}
  </section>;
}

function RevisionSettings({ revision }: { revision: ConfiguredLogicRevisionResponse }) {
  const lines = describeSettings(revision.parametersJson, revision.moduleManifestJson);
  if (!lines && !revision.view) return null;
  return <dl className="revision-settings" aria-label={t('setup.revisionSettings')}>
    {revision.view && <div><dt>{t('setup.targetView')}</dt><dd>{t('setup.viewSnapshot', {
      revision: revision.view.revision ?? 0,
      count: revision.assetBindings?.filter(binding => binding.role?.toLowerCase() === revision.view?.role?.toLowerCase()).length ?? 0,
    })}</dd></div>}
    {lines?.map(line => <div key={line.label}><dt>{line.label}</dt><dd>{line.value}</dd></div>)}
  </dl>;
}
