import { useEffect, useRef, useState } from 'react';
import type { ConfiguredLogicRevisionResponse, ExecutionResponse, ConfiguredShadowReceiptResponse } from '../api/generated/v1/types.gen';
import { isSecurityId } from '../security/identifiers';
import { t } from '../localization';
import { fetchExecutionDetail, ExecutionDetailError } from './executionDetail';
import { ExecutionDetailPanel } from './ExecutionDetailPanel';
import { setupResponse, SetupApiError } from './configuredLogicSetup';
import './configuredShadow.css';

type Props = {
  userId: string; configurationId: string; revision: ConfiguredLogicRevisionResponse;
  canRequest: boolean; disabled?: boolean;
};
const terminal = new Set(['Completed', 'Failed', 'Cancelled', 'Suspended', 'TimedOut']);
export function shadowRequestStorageKey(userId: string, configurationId: string, revisionId: string) {
  return `wsa.shadow-request.v1:${userId}:${configurationId}:${revisionId}`;
}
function requestReceipt(value: unknown, configurationId: string, revisionId: string): value is ConfiguredShadowReceiptResponse & { executionId: string } {
  if (!value || typeof value !== 'object') return false;
  const r = value as ConfiguredShadowReceiptResponse;
  return isSecurityId(r.executionId) && r.logicInstanceId === configurationId &&
    r.configurationRevisionId === revisionId && r.requestContractVersion === 2 && r.mode === 'Shadow' && r.trigger === 'Manual';
}
function receipt(value: unknown, configurationId: string, revisionId: string): value is ExecutionResponse & { executionId: string } {
  if (!value || typeof value !== 'object') return false;
  const r = value as ExecutionResponse;
  return isSecurityId(r.executionId) && r.logicInstanceId === configurationId &&
    r.configurationRevisionId === revisionId && r.requestContractVersion === 2 &&
    r.mode === 'Shadow' && r.trigger === 'Manual' &&
    ['Requested', 'Queued', 'Starting', 'Running', 'Waiting', ...terminal].includes(r.status ?? '');
}
/** A request key is saved before the POST and survives navigation/reload.
 * Explicit recovery repeats that same idempotent command, never a new key. */
export function ConfiguredShadowPanel({ userId, configurationId, revision, canRequest, disabled }: Props) {
  const revisionId = revision.id ?? '';
  const storageKey = shadowRequestStorageKey(userId, configurationId, revisionId);
  const [requestId, setRequestId] = useState<string | null>(() => {
    try { const key = sessionStorage.getItem(storageKey); return key === null ? null : isSecurityId(key) ? key : 'invalid'; }
    catch { return null; }
  });
  const [review, setReview] = useState(false);
  const [busy, setBusy] = useState(false);
  const [execution, setExecution] = useState<(ExecutionResponse & { executionId: string }) | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [denied, setDenied] = useState(false);
  const pending = useRef(false);
  const request = useRef<AbortController | null>(null);
  useEffect(() => () => request.current?.abort(), []);
  async function submit() {
    if (pending.current || !canRequest || disabled || denied || !isSecurityId(revisionId)) return;
    let key = requestId;
    if (key && !isSecurityId(key)) { setError(t('shadow.storageCorrupt')); return; }
    if (!key) {
      try {
        if (typeof crypto.randomUUID === 'function') key = crypto.randomUUID();
        else {
          const bytes = crypto.getRandomValues(new Uint8Array(16));
          bytes[6] = (bytes[6] & 15) | 64; bytes[8] = (bytes[8] & 63) | 128;
          const hex = Array.from(bytes, b => b.toString(16).padStart(2, '0')).join('');
          key = `${hex.slice(0,8)}-${hex.slice(8,12)}-${hex.slice(12,16)}-${hex.slice(16,20)}-${hex.slice(20)}`;
        }
      } catch { setError(t('shadow.storageUnavailable')); return; }
      // Do not dispatch if the recovery token cannot be persisted first.
      try { sessionStorage.setItem(storageKey, key); }
      catch { setError(t('shadow.storageUnavailable')); return; }
      setRequestId(key);
    }
    const c = new AbortController(); request.current = c;
    pending.current = true; setBusy(true); setError(null);
    try {
      const result = await setupResponse(await fetch('/api/v1/configured-logic/' + configurationId +
        '/revisions/' + revisionId + '/shadow', { method: 'POST', signal: c.signal,
        credentials: 'same-origin', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ requestId: key }) }), 200, true);
      if (!requestReceipt(result, configurationId, revisionId)) throw new SetupApiError('uncertain');
      const verified = await fetchExecutionDetail(result.executionId, c.signal);
      if (!receipt(verified, configurationId, revisionId) || verified.executionId !== result.executionId)
        throw new SetupApiError('uncertain');
      if (!c.signal.aborted) { setExecution(verified); setReview(false); }
    } catch (reason) {
      if (c.signal.aborted) return;
      const e = reason instanceof SetupApiError ? reason
        : reason instanceof ExecutionDetailError && ['accessDenied', 'notFound'].includes(reason.kind)
          ? new SetupApiError('denied') : new SetupApiError('uncertain');
      if (e.kind === 'denied') { setDenied(true); setExecution(null); }
      setError(t(e.kind === 'denied' ? 'shadow.denied' : e.kind === 'conflict' ? 'shadow.conflict' : 'shadow.uncertain') +
        (e.code ? ` (${e.code})` : ''));
    } finally { pending.current = false; if (!c.signal.aborted) setBusy(false); }
  }
  return <section className="shadow-panel" aria-label={t('shadow.heading')}>
    <h4>{t('shadow.heading')}</h4>
    <p>{t('shadow.scope', { number: revision.revisionNumber ?? '—' })}</p>
    {!canRequest && <p>{t('shadow.readOnly')}</p>}
    {canRequest && !requestId && !review && <button type="button" disabled={disabled || busy || denied}
      onClick={() => setReview(true)}>{t('shadow.review')}</button>}
    {review && !requestId && <div role="group" aria-label={t('shadow.confirmHeading')}>
      <p>{t('shadow.confirmText')}</p>
      <button type="button" disabled={disabled || busy || denied} onClick={() => void submit()}>{t('shadow.confirm')}</button>
      <button type="button" disabled={busy} onClick={() => setReview(false)}>{t('shadow.cancel')}</button>
    </div>}
    {requestId && !execution && <div>
      <p>{t('shadow.recoveryHint')}</p><code>{requestId}</code>
      <button type="button" disabled={disabled || busy || denied || !canRequest}
        onClick={() => void submit()}>{t('shadow.recover')}</button>
    </div>}
    {busy && <p role="status">{t('shadow.submitting')}</p>}
    {error && <p role="alert">{error}</p>}
    {execution && <>
      <ExecutionDetailPanel key={execution.executionId} executionId={execution.executionId} poll
        onObserved={value => { if (receipt(value, configurationId, revisionId)) setExecution(value); }}
        onAccessLost={() => { setExecution(null); setDenied(true); setError(t('shadow.denied')); }}
        onClose={() => setExecution(null)} />
      {terminal.has(execution.status ?? '') && <button type="button" disabled={disabled || busy || !canRequest}
        onClick={() => {
          try { sessionStorage.removeItem(storageKey); }
          catch { setError(t('shadow.storageUnavailable')); return; }
          setRequestId(null); setExecution(null); setReview(false); setError(null);
        }}>{t('shadow.newRun')}</button>}
    </>}
  </section>;
}
