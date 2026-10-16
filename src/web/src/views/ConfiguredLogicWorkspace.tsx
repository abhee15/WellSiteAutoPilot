import { useEffect, useRef, useState } from 'react';
import type { AssetResponse, AssetTypeResponse, ConfiguredLogicRevisionResponse,
  LogicModuleResponse } from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { ConfiguredShadowPanel } from './ConfiguredShadowPanel';
import { ConfiguredLogicSetupPanel } from './ConfiguredLogicSetupPanel';
import { ConfiguredLogicRevisionOverview } from './ConfiguredLogicRevisionOverview';
import { readConfiguration, setupResponse, SetupApiError } from './configuredLogicSetup';
import type { VerifiedConfiguration } from './configuredLogicSetup';

type Props = {
  selectedId: string | null; canManage: boolean; userId?: string | null; canRequestShadow?: boolean; modules: LogicModuleResponse[];
  assets: AssetResponse[]; assetTypes: AssetTypeResponse[];
  onClose: () => void; onChanged: () => Promise<void>;
};
/** A fresh authorized detail is the mutation boundary. Polling list metadata
 * cannot silently replace the revision a user is reviewing. */
export function ConfiguredLogicWorkspace({ selectedId, canManage, userId, canRequestShadow, modules,
  assets, assetTypes, onClose, onChanged }: Props) {
  const [detail, setDetail] = useState<VerifiedConfiguration | null>(null);
  const [editing, setEditing] = useState(!selectedId);
  const [source, setSource] = useState<ConfiguredLogicRevisionResponse | undefined>();
  const [busy, setBusy] = useState(false);
  const [blocked, setBlocked] = useState(false);
  const [denied, setDenied] = useState(false);
  const [confirmId, setConfirmId] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
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
    if (!detail || !revision.id || !canManage || busy || blocked || pending.current) return;
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
    {!detail && selectedId && !error && <p role="status">{t('setup.loading')}</p>}
    {detail && <>
      <ConfiguredLogicRevisionOverview logic={detail} onClose={onClose} />
      <p>{t('setup.immutable')}</p>
      <ul className="setup-revisions">{[...detail.revisions].sort((a,b) =>
        (b.revisionNumber ?? 0) - (a.revisionNumber ?? 0)).map(r => <li key={r.id}>
        <strong>{t('setup.revision', { number: r.revisionNumber ?? '—' })} · {r.status}</strong>
        <p>{t('setup.exactScope', { assets: r.assetBindings?.map(b =>
          assets.find(a => a.id === b.assetId)?.name ?? b.assetId).join(', ') ?? '' })}</p>
        {canManage && !editing && <div className="setup-actions">
          <button type="button" disabled={busy || blocked} onClick={() => {
            setSource(r); setEditing(true); setConfirmId(null);
          }}>{t('setup.copyRevision', { number: r.revisionNumber ?? '—' })}</button>
          {r.status === 'Draft' && <button type="button" disabled={busy || blocked}
            onClick={() => void transition(r, 'validate')}>{t('setup.validate', { number: r.revisionNumber ?? '—' })}</button>}
          {r.status === 'Validated' && <button type="button" disabled={busy || blocked}
            onClick={() => setConfirmId(r.id)}>{t('setup.reviewActivation', { number: r.revisionNumber ?? '—' })}</button>}
          {confirmId === r.id && <div role="group" aria-label={t('setup.confirmHeading')}>
            <p>{t('setup.confirmText', { number: r.revisionNumber ?? '—' })}</p>
            <button type="button" disabled={busy || blocked} onClick={() => void transition(r, 'activate')}>{t('setup.confirm')}</button>
            <button type="button" disabled={busy} onClick={() => setConfirmId(null)}>{t('setup.cancelActivation')}</button>
          </div>}
        </div>}
        {userId && r.status === 'Active' && r.id === detail.activeRevisionId && !editing && <ConfiguredShadowPanel
          key={r.id} userId={userId} configurationId={detail.id} revision={r}
          canRequest={canRequestShadow === true} disabled={busy || blocked} />}
      </li>)}</ul>
    </>}
    {(detail || selectedId) && !editing && <button type="button" disabled={busy}
      onClick={() => void reload()}>{t('setup.reload')}</button>}
    {editing && canManage && !denied && (!selectedId || detail) && <ConfiguredLogicSetupPanel
      modules={modules} assets={assets} assetTypes={assetTypes}
      configuration={detail ?? undefined} revision={source}
      onSaved={value => { setDetail(value); setEditing(false); setSource(undefined);
        setNotice(t('setup.saved')); void onChanged(); }}
      onDenied={committed => { setDetail(null); setEditing(false); setSource(undefined);
        setDenied(true); setBlocked(true); setError(t(committed ? 'setup.committed' : 'setup.denied')); }}
      onCancel={() => { setEditing(false); setSource(undefined); if (!detail) onClose(); }} />}
    {!detail && <button type="button" disabled={busy} onClick={onClose}>{t('configured.close')}</button>}
  </section>;
}
