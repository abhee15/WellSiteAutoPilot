import { ActionButton } from '../components/ActionButton';
import { RefreshCw } from 'lucide-react';
import { useCallback, useEffect, useId, useRef, useState } from 'react';
import type { AssetResponse, AssetTypeResponse, ConfiguredLogicRevisionResponse, ExecutionResponse,
  LogicModuleResponse } from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { ConfiguredShadowPanel } from './ConfiguredShadowPanel';
import { ConfiguredLogicSetupPanel } from './ConfiguredLogicSetupPanel';
import { AutonomyPolicyPanel } from './AutonomyPolicyPanel';
import { orderedRevisions } from './configuredLogicViews';
import { ConfiguredLogicRevisionOverview } from './ConfiguredLogicRevisionOverview';
import { ConfiguredLogicSchedulePanel } from './ConfiguredLogicSchedulePanel';
import { readConfiguration, setupResponse, SetupApiError } from './configuredLogicSetup';
import type { VerifiedConfiguration } from './configuredLogicSetup';
import { LoadingIndicator } from '../components/LoadingIndicator';
import { CloseButton } from '../components/CloseButton';
import { Status } from '../components/Status';
import { DetailTabs } from '../components/DetailTabs';
import { ConfiguredRevisionDetails } from './ConfiguredRevisionDetails';
import { ExecutionHistory } from './ExecutionHistory';
import { fetchConfigurationActivity } from './configuredLogicActivity';
import { useLiveResource } from './useLiveResource';
import './configuredLogicDetail.css';

type VerifiedRevision = VerifiedConfiguration['revisions'][number];
type Props = {
  selectedId: string | null; canManage: boolean; userId?: string | null; canRequestShadow?: boolean; modules: LogicModuleResponse[];
  assets: AssetResponse[]; assetTypes: AssetTypeResponse[]; executions?: readonly ExecutionResponse[];
  onClose: () => void; onChanged: () => Promise<void>;
  initialAssetId?: string | null; canReadViews?: boolean;
  /** recommendations.read: may see the autonomy policy. control.manage: may change it. */
  canReadControl?: boolean; canManageControl?: boolean;
};
/** A fresh authorized detail is the mutation boundary. Polling list metadata
 * cannot silently replace the revision a user is reviewing. */
export function ConfiguredLogicWorkspace({ selectedId, canManage, userId, canRequestShadow, modules,
  assets, assetTypes, executions, onClose, onChanged, initialAssetId, canReadViews = false, canReadControl = false,
  canManageControl = false }: Props) {
  const [detail, setDetail] = useState<VerifiedConfiguration | null>(null);
  const [editing, setEditing] = useState(!selectedId);
  const [tab, setTab] = useState<'overview' | 'configuration' | 'activity'>('overview');
  const [revisionId, setRevisionId] = useState<string | null>(null);
  const [showShadow, setShowShadow] = useState(false);
  const [activityOpened, setActivityOpened] = useState(false);
  const prefix = useId();
  const [editDraft, setEditDraft] = useState(false);
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

  const loadActivity = useCallback((signal: AbortSignal) => fetchConfigurationActivity(detail?.id ?? '', signal), [detail?.id]);
  const activity = useLiveResource(`${detail?.id}:${detail?.activeRevisionId}`, loadActivity,
    ['executions', 'configured-logic'], detail !== null && !denied);
  const changedElsewhere = Boolean(detail && activity.data && activity.data.activeRevisionId !== detail.activeRevisionId);
  const mutationBlocked = busy || blocked || changedElsewhere;
  const ordered = detail ? orderedRevisions(detail) : [];
  const active = ordered.find(r => r.id === detail?.activeRevisionId);
  const selected = ordered.find(r => r.id === revisionId) ?? active ?? ordered[0];
  const waiting = ordered.filter(r => r.id !== detail?.activeRevisionId && (r.status === 'Draft' || r.status === 'Validated'));
  const earlier = ordered.filter(r => r.id !== detail?.activeRevisionId && !waiting.includes(r));
  const activeAssetCount = new Set(active?.assetBindings?.map(binding => binding.assetId) ?? []).size;
  const contextKey = changedElsewhere
    ? activeAssetCount === 1 ? 'logicDetail.reviewedContextOne' : 'logicDetail.reviewedContext'
    : activeAssetCount === 1 ? 'logicDetail.activeContextOne' : 'logicDetail.activeContext';
  const canReadRuns = executions !== undefined;
  useEffect(() => {
    if (!activity.denied) return;
    request.current?.abort(); setDetail(null); setDenied(true); setEditing(false); setSource(undefined);
    setBlocked(true); setBusy(false); setError(t('setup.denied'));
  }, [activity.denied]);
  useEffect(() => {
    if (detail && editing && !mayManage) { setEditing(false); setSource(undefined); }
  }, [detail, editing, mayManage]);
  useEffect(() => { if (!canReadRuns && tab === 'activity') setTab('overview'); }, [canReadRuns, tab]);
  function selectTab(next: typeof tab) {
    setTab(next); setConfirmId(null);
    if (next === 'activity') setActivityOpened(true);
  }
  function copyRevision(revision: VerifiedRevision) {
    if (mutationBlocked || revision.mode !== 'Shadow') return;
    setEditDraft(false); setSource(revision); setEditing(true); setTab('configuration'); setConfirmId(null); setShowShadow(false);
  }

  function editRevision(revision: VerifiedRevision) {
    if (mutationBlocked || revision.status !== 'Draft' || revision.mode !== 'Shadow') return;
    setEditDraft(true); setSource(revision); setEditing(true); setTab('configuration'); setConfirmId(null); setShowShadow(false);
  }

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
    if (!detail || !revision.id || !mayManage || mutationBlocked || pending.current) return;
    const snapshot = detail;
    pending.current = true; setBusy(true); setError(null); setNotice(null);
    const c = new AbortController(); request.current?.abort(); request.current = c;
    let committed = false;
    try {
      const result = await setupResponse(await fetch('/api/v1/configured-logic/' + snapshot.id +
        '/revisions/' + revision.id + '/' + action, { method: 'POST', credentials: 'same-origin',
        signal: c.signal, headers: action === 'activate'
          ? { 'If-Match': '"' + (snapshot.activeRevisionId ?? 'none') + '"' }
          : { 'If-Match': '"' + (revision.draftVersion ?? 0) + '"' } }), 200, true);
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
      setBlocked(committed || e.kind !== 'invalid'); setConfirmId(null);
      if (e.kind === 'denied') { setDetail(null); setDenied(true); setSource(undefined); setEditing(false); }
      setError(t(committed ? 'setup.committed' : `setup.${e.kind}`) + (e.code ? ` (${e.code})` : ''));
    } finally { pending.current = false; if (!c.signal.aborted) setBusy(false); }
  }
  const editor = (editing && mayManage && !denied && (!selectedId || detail) && <ConfiguredLogicSetupPanel
      modules={modules} assets={assets} assetTypes={assetTypes} canReadViews={canReadViews}
      configuration={detail ?? undefined} revision={source} editDraft={editDraft} initialAssetId={initialAssetId}
      onSaved={(value, savedRevisionId) => { setDetail(value); setRevisionId(savedRevisionId ?? orderedRevisions(value)[0]?.id ?? null); setTab('configuration');
        setEditing(false); setSource(undefined); setNotice(t('setup.saved')); void onChanged(); }}
      onDenied={committed => { setDetail(null); setEditing(false); setSource(undefined);
        setDenied(true); setBlocked(true); setError(t(committed ? 'setup.committed' : 'setup.denied')); }}
      onCancel={() => { setEditing(false); setSource(undefined); if (!detail) onClose(); }} />);
  return <section className="setup-panel configured-detail" aria-label={t('setup.workspace')}>
    {notice && <p role="status">{notice}</p>}
    {error && <p role="alert">{error}</p>}
    {!detail && selectedId && !error && <LoadingIndicator label={t('setup.loading')} />}
    {detail && <>
      <header className="logic-detail-header">
        <div><h3>{detail.name}</h3><p className="logic-status-sentence">{active
          ? t(contextKey, { number: active.revisionNumber ?? '—', mode: active.mode ?? '—', count: activeAssetCount })
          : t('configured.noLive')}</p>
          {waiting.length > 0 && <p className="logic-detail-note">{t(waiting.length === 1 ? 'logicDetail.savedDraftOne' : 'logicDetail.savedDrafts', { count: waiting.length })}</p>}
        </div><CloseButton onClick={onClose} />
      </header>
      {changedElsewhere && <p role="status">{t('logicDetail.changedElsewhere')}</p>}
      <div className="setup-actions">
        {mayManage && (active ?? ordered[0])?.mode === 'Shadow' && <ActionButton label={t('logicDetail.createRevision')}
          disabled={mutationBlocked || editing} onClick={() => copyRevision((active ?? ordered[0])!)} />}
        {active?.mode === 'Shadow' && userId && canRequestShadow && detail.canRequestShadow && <ActionButton
          label={t('logicDetail.runActive', { number: active.revisionNumber ?? '—' })} disabled={mutationBlocked || editing}
          onClick={() => { selectTab('overview'); setShowShadow(true); }} />}
        {!editing && <ActionButton icon={RefreshCw} iconOnly label={t('setup.reload')} disabled={busy} onClick={() => void reload()} />}
      </div>
      <DetailTabs prefix={prefix} label={t('logicDetail.sections')}
        tabs={[{ id: 'overview', label: t('logicDetail.overview') }, { id: 'configuration', label: t('logicDetail.configuration') },
          ...(canReadRuns ? [{ id: 'activity' as const, label: t('logicDetail.activity') }] : [])]}
        selected={tab} disabled={editing || busy} onSelect={selectTab} />
      <div id={`${prefix}-overview`} role="tabpanel" aria-labelledby={`${prefix}-tab-overview`} hidden={tab !== 'overview'}>
        <ConfiguredLogicRevisionOverview logic={detail} assets={assets} activity={activity.data}
          loading={activity.loading} failed={activity.failed} canReadRuns={canReadRuns}
          onHistory={() => selectTab('activity')} onRetry={activity.refresh} />
        <ConfiguredLogicSchedulePanel configuredLogicId={detail.id} activeRevisionId={detail.activeRevisionId}
          mayManage={mayManage} disabled={mutationBlocked || editing} />
        {active?.mode === 'Shadow' && userId && <div hidden={!showShadow || editing}>
          <h4>{t('logicDetail.runActive', { number: active.revisionNumber ?? '—' })}</h4>
          <ConfiguredShadowPanel key={active.id} userId={userId} configurationId={detail.id} revision={active}
            canRequest={canRequestShadow === true && detail.canRequestShadow === true} disabled={mutationBlocked || editing} />
        </div>}
      </div>
      <div id={`${prefix}-configuration`} role="tabpanel" aria-labelledby={`${prefix}-tab-configuration`} hidden={tab !== 'configuration'}>
        <p className="logic-detail-note">{t('setup.immutable')}</p>
        {editing && <p role="status">{t('logicDetail.editingNotice')}</p>}
        {!editing && selected && <>
          <label className="logic-revision-select">{t('logicDetail.selectRevision')}
            <select value={selected.id} disabled={busy} onChange={event => { setRevisionId(event.target.value); setConfirmId(null); }}>
              <optgroup label={t('logicDetail.activeAndDrafts')}>
                {active && <option value={active.id}>{t('setup.revision', { number: active.revisionNumber ?? '—' })} · {active.status}</option>}
                {waiting.map(r => <option key={r.id} value={r.id}>{t('setup.revision', { number: r.revisionNumber ?? '—' })} · {r.status}</option>)}
              </optgroup>
              {earlier.length > 0 && <optgroup label={t('configured.earlier', { count: earlier.length })}>
                {earlier.map(r => <option key={r.id} value={r.id}>{t('setup.revision', { number: r.revisionNumber ?? '—' })} · {r.status}</option>)}
              </optgroup>}
            </select>
          </label>
          <section className="logic-detail-section" aria-label={t('logicDetail.selectedRevision')}>
            <div className="logic-detail-row"><h4>{t('setup.revision', { number: selected.revisionNumber ?? '—' })} · {selected.status}</h4>
              <Status value={selected.status} /></div>
            <ConfiguredRevisionDetails revision={selected} assets={assets} />
            {mayManage && <div className="setup-actions">
              <ActionButton label={t('setup.copyRevision', { number: selected.revisionNumber ?? '—' })}
                disabled={mutationBlocked || selected.mode !== 'Shadow'} onClick={() => copyRevision(selected)} />
              {selected.mode !== 'Shadow' && <p className="logic-detail-note">{t('logicDetail.authoringMode')}</p>}
              {selected.status === 'Draft' && selected.mode === 'Shadow' && <ActionButton label={t('draftEditor.edit', { number: selected.revisionNumber ?? '—' })}
                disabled={mutationBlocked} onClick={() => editRevision(selected)} />}
              {selected.status === 'Draft' && <ActionButton label={t('setup.validate', { number: selected.revisionNumber ?? '—' })}
                disabled={mutationBlocked} onClick={() => void transition(selected, 'validate')} />}
              {selected.status === 'Validated' && <ActionButton label={t('setup.reviewActivation', { number: selected.revisionNumber ?? '—' })}
                disabled={mutationBlocked} onClick={() => setConfirmId(selected.id)} />}
              {confirmId === selected.id && <div role="group" aria-label={t('setup.confirmHeading')}>
                <p>{t('setup.confirmText', { number: selected.revisionNumber ?? '—' })}</p>
                <ActionButton label={t('setup.confirm')} disabled={mutationBlocked} onClick={() => void transition(selected, 'activate')} />
                <ActionButton label={t('setup.cancelActivation')} disabled={busy} onClick={() => setConfirmId(null)} />
              </div>}
            </div>}
          </section>
          {canReadControl && <AutonomyPolicyPanel key={detail.id} configuredLogicId={detail.id}
            canManage={canManageControl} disabled={mutationBlocked} />}
        </>}
        {editor}
      </div>
      {canReadRuns && <div id={`${prefix}-activity`} role="tabpanel" aria-labelledby={`${prefix}-tab-activity`} hidden={tab !== 'activity'}>
        <h4>{t('logicDetail.historyHeading')}</h4>
        {activityOpened && <ExecutionHistory logicId={detail.id} assets={assets} configuredLogic={[detail]} enabled={tab === 'activity'} />}
      </div>}
    </>}
    {!detail && editor}
    {!detail && <ActionButton label={t('configured.close')} disabled={busy} onClick={onClose} />}
  </section>;
}
