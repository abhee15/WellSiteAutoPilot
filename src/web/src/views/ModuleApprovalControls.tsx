import { useId, useRef, useState } from 'react';
import type { LogicModuleResponse } from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { SetupApiError, setupResponse } from './configuredLogicSetup';
import {
  approvalPath, approvalTookEffect, availableApproval, MAXIMUM_REASON_LENGTH, moduleState, reasonProblem,
} from './moduleApproval';
import type { ModuleApprovalAction } from './moduleApproval';
import './moduleApproval.css';

const REASON_TEXT = { empty: 'moduleApproval.reasonEmpty', long: 'moduleApproval.reasonLong',
  characters: 'moduleApproval.reasonCharacters' } as const;

/** Says where a module version stands and, for people who may manage modules, offers the one change
 * that is possible. The change is explained, needs a reason, and is verified against the server's reply. */
export function ModuleApprovalControls({ module, canManage, onChanged }: {
  module: LogicModuleResponse;
  canManage: boolean;
  onChanged: () => Promise<void>;
}) {
  const state = moduleState(module);
  const action = availableApproval(state);
  const [reviewing, setReviewing] = useState<ModuleApprovalAction | null>(null);
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const pending = useRef(false);
  const reasonId = useId();
  const name = module.displayName ?? '—';
  const problem = reasonProblem(reason);

  async function submit(chosen: ModuleApprovalAction) {
    if (pending.current || problem) return;
    pending.current = true; setBusy(true); setError(null); setNotice(null);
    let message: string | null = null;
    try {
      const receipt = await setupResponse(await fetch(approvalPath(module, chosen), {
        method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ reason: reason.trim() }),
      }), 200, true);
      if (!approvalTookEffect(module, receipt, chosen)) throw new SetupApiError('uncertain');
      setNotice(t(chosen === 'approve' ? 'moduleApproval.approvedDone' : 'moduleApproval.retiredDone'));
      setReviewing(null); setReason('');
    } catch (failure) {
      const known = failure instanceof SetupApiError ? failure : new SetupApiError('uncertain');
      message = known.code === 'MODULE_RETIRED' ? t('moduleApproval.retiredCode')
        : known.code === 'MODULE_NOT_APPROVED' ? t('moduleApproval.notApprovedCode')
          : t(`moduleApproval.${known.kind}`);
      setError(message); setReviewing(null);
    } finally {
      pending.current = false; setBusy(false);
      // Either way the list now shows what is really stored, so nobody acts on a stale state.
      void onChanged();
    }
  }

  return (
    <div className="module-approval" role="group" aria-label={t('moduleApproval.group', { name })}>
      <p>{t(`moduleApproval.${state}`)}</p>
      {notice && <p role="status">{notice}</p>}
      {error && <p role="alert">{error}</p>}
      {canManage && action && !reviewing && (
        <button type="button" disabled={busy}
          onClick={() => { setReviewing(action); setReason(''); setError(null); setNotice(null); }}>
          {t(action === 'approve' ? 'moduleApproval.approve' : 'moduleApproval.retire')}
        </button>
      )}
      {canManage && reviewing && (
        <div className="module-approval-review" role="group"
          aria-label={t(reviewing === 'approve' ? 'moduleApproval.approveHeading' : 'moduleApproval.retireHeading',
            { name, version: module.version ?? '—' })}>
          <h4>{t(reviewing === 'approve' ? 'moduleApproval.approveHeading' : 'moduleApproval.retireHeading',
            { name, version: module.version ?? '—' })}</h4>
          <p>{t(reviewing === 'approve' ? 'moduleApproval.approveText' : 'moduleApproval.retireText')}</p>
          <label htmlFor={reasonId}>{t('moduleApproval.reason')}</label>
          <textarea id={reasonId} rows={2} maxLength={MAXIMUM_REASON_LENGTH + 100} value={reason} disabled={busy}
            aria-describedby={`${reasonId}-hint`} aria-invalid={problem !== null && problem !== 'empty'}
            onChange={event => setReason(event.target.value)} />
          <p id={`${reasonId}-hint`} className="module-approval-hint">
            {problem ? t(REASON_TEXT[problem]) : ''}
          </p>
          <div className="module-approval-actions">
            <button type="button" disabled={busy || problem !== null} onClick={() => void submit(reviewing)}>
              {busy ? t('moduleApproval.working')
                : t(reviewing === 'approve' ? 'moduleApproval.confirmApprove' : 'moduleApproval.confirmRetire')}
            </button>
            <button type="button" disabled={busy} onClick={() => setReviewing(null)}>{t('moduleApproval.cancel')}</button>
          </div>
        </div>
      )}
    </div>
  );
}
