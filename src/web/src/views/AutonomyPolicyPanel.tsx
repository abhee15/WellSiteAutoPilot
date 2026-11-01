import { useCallback, useEffect, useRef, useState } from 'react';
import { useChangeRefresh } from '../live/LiveUpdates';
import { ActionButton } from '../components/ActionButton';
import { Status } from '../components/Status';
import { t } from '../localization';
import { formatDetailTimestamp } from './executionDetail';
import {
  AutonomyPolicyError, POLICY_ACTIONS_MAX, POLICY_COMMAND_MAX, POLICY_MAX_LIMITS,
  POLICY_UNIT_MAX, POLICY_WINDOW_MAX_SECONDS, draftFromPolicy, fetchAutonomyPolicy, saveAutonomyPolicy,
  validatePolicyDraft,
} from './autonomyPolicy';
import type {
  AutonomyLimitDraft, AutonomyPolicy, AutonomyPolicyDraft, AutonomyPolicyErrorKind, PolicyIssue,
} from './autonomyPolicy';

type Props = {
  configuredLogicId: string;
  /** control.manage: may change the policy. Everyone who can read recommendations sees it. */
  canManage: boolean;
  disabled?: boolean;
};

/** The message for an issue; row issues name the row so the person can find it. */
export function policyIssueMessage(issue: PolicyIssue): string {
  const row = 'index' in issue ? issue.index + 1 : 0;
  return t(`autonomyPolicy.issue.${issue.code}`, { row });
}

/**
 * What a configured logic is allowed to command on its own: which commands, within which bounds, how often. A
 * configuration without a policy may command nothing. Saving replaces the policy as a whole.
 */
export function AutonomyPolicyPanel({ configuredLogicId, canManage, disabled = false }: Props) {
  const [policy, setPolicy] = useState<AutonomyPolicy | null>(null);
  const [loaded, setLoaded] = useState(false);
  const [loadError, setLoadError] = useState<AutonomyPolicyErrorKind | null>(null);
  const [draft, setDraft] = useState<AutonomyPolicyDraft | null>(null);
  const [issues, setIssues] = useState<PolicyIssue[]>([]);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<AutonomyPolicyErrorKind | null>(null);
  const [saved, setSaved] = useState(false);
  const refreshRef = useRef<AbortController | null>(null);
  const saveRef = useRef<AbortController | null>(null);

  const refresh = useCallback(async () => {
    refreshRef.current?.abort();
    const controller = new AbortController();
    refreshRef.current = controller;
    try {
      const next = await fetchAutonomyPolicy(configuredLogicId, controller.signal);
      if (controller.signal.aborted) return;
      setPolicy(next);
      setLoaded(true);
      setLoadError(null);
    } catch (failure) {
      if (controller.signal.aborted) return;
      // Never keep showing a policy that can no longer be confirmed.
      setPolicy(null);
      setLoaded(false);
      setLoadError(failure instanceof AutonomyPolicyError ? failure.kind : 'unavailable');
    }
  }, [configuredLogicId]);

  useEffect(() => {
    void refresh();
    return () => {
      refreshRef.current?.abort();
      saveRef.current?.abort();
    };
  }, [refresh]);
  // Not while editing: a change signal must not replace what the person is typing; they see the result on save.
  useChangeRefresh(['control'], () => { if (!draft) void refresh(); });

  const errors: Record<AutonomyPolicyErrorKind, string> = {
    invalid: t('autonomyPolicy.invalid'),
    accessDenied: t('autonomyPolicy.accessDenied'),
    notFound: t('autonomyPolicy.unavailable'),
    unavailable: t('autonomyPolicy.unavailable'),
    invalidResponse: t('autonomyPolicy.invalidResponse'),
  };

  const edit = (change: Partial<AutonomyPolicyDraft>) => {
    setDraft(current => (current ? { ...current, ...change } : current));
    setIssues([]);
    setSaveError(null);
  };
  const editLimit = (index: number, change: Partial<AutonomyLimitDraft>) => {
    if (!draft) return;
    edit({ limits: draft.limits.map((item, at) => (at === index ? { ...item, ...change } : item)) });
  };
  const close = () => {
    setDraft(null);
    setIssues([]);
    setSaveError(null);
  };

  const submit = async () => {
    if (!draft || saving) return;
    const checked = validatePolicyDraft(draft);
    if (!checked.request) {
      setIssues(checked.issues);
      return;
    }
    saveRef.current?.abort();
    const controller = new AbortController();
    saveRef.current = controller;
    setSaving(true);
    setSaveError(null);
    try {
      const next = await saveAutonomyPolicy(configuredLogicId, checked.request, controller.signal);
      if (controller.signal.aborted) return;
      setPolicy(next);
      setLoaded(true);
      setSaved(true);
      close();
    } catch (failure) {
      if (controller.signal.aborted) return;
      setSaveError(failure instanceof AutonomyPolicyError ? failure.kind : 'unavailable');
      // The stored policy may differ from what was asked for; show what is true now.
      void refresh();
    } finally {
      if (saveRef.current === controller) {
        saveRef.current = null;
        setSaving(false);
      }
    }
  };

  if (!loaded) {
    return (
      <section className="panel autonomy-policy" aria-label={t('autonomyPolicy.heading')}>
        <div className="panel-header"><h4>{t('autonomyPolicy.heading')}</h4></div>
        <p role={loadError ? 'alert' : 'status'} className={loadError ? 'execution-detail-error' : undefined}>
          {loadError ? errors[loadError] : t('autonomyPolicy.loading')}
        </p>
      </section>
    );
  }

  const blocked = disabled || saving;
  return (
    <section className="panel autonomy-policy" aria-label={t('autonomyPolicy.heading')}>
      <div className="panel-header">
        <h4>{t('autonomyPolicy.heading')}</h4>
        <Status value={policy?.enabled ? t('autonomyPolicy.enabled') : t('autonomyPolicy.off')}
          tone={policy?.enabled ? 'success' : 'neutral'} />
      </div>
      <p>{t('autonomyPolicy.scope')}</p>
      {saved && !draft && <p role="status">{t('autonomyPolicy.saved')}</p>}
      {!policy && <p>{t('autonomyPolicy.none')}</p>}
      {policy && !draft && (
        <>
          {policy.limits.length === 0
            ? <p>{t('autonomyPolicy.noCommands')}</p>
            : (
              <ul className="autonomy-policy-limits">
                {policy.limits.map(limit => (
                  <li key={limit.command}>
                    <strong>{limit.command}</strong>{' '}
                    {t('autonomyPolicy.bounds', {
                      min: limit.minimumValue ?? '—', max: limit.maximumValue ?? '—',
                      unit: limit.unit ?? t('autonomyPolicy.anyUnit'),
                    })}
                  </li>
                ))}
              </ul>
            )}
          <p>{t('autonomyPolicy.rate', { actions: policy.maxActionsPerWindow, seconds: policy.windowSeconds })}</p>
          <p>{policy.allowApprovedRecommendations ? t('autonomyPolicy.approvedAllowed') : t('autonomyPolicy.approvedNotAllowed')}</p>
          <p><small>{t('autonomyPolicy.changedBy', { by: policy.updatedBy, at: formatDetailTimestamp(policy.updatedAtUtc) })}</small></p>
        </>
      )}
      {canManage && !draft && (
        <div className="execution-detail-actions">
          <ActionButton label={policy ? t('autonomyPolicy.edit') : t('autonomyPolicy.create')} disabled={blocked}
            onClick={() => { setSaved(false); setDraft(draftFromPolicy(policy)); }} />
        </div>
      )}
      {canManage && draft && (
        <form className="autonomy-policy-form" onSubmit={event => { event.preventDefault(); void submit(); }}>
          <label className="recommendation-review-confirm">
            <input type="checkbox" checked={draft.enabled} disabled={saving}
              onChange={event => edit({ enabled: event.target.checked })} />
            {t('autonomyPolicy.enabledLabel')}
          </label>
          <fieldset disabled={saving}>
            <legend>{t('autonomyPolicy.commandsLegend')}</legend>
            {draft.limits.map((limit, index) => (
              <div className="autonomy-policy-row" role="group" key={index}
                aria-label={t('autonomyPolicy.row', { row: index + 1 })}>
                <label><span>{t('autonomyPolicy.command')}</span>
                  <input value={limit.command} maxLength={POLICY_COMMAND_MAX}
                    onChange={event => editLimit(index, { command: event.target.value })} /></label>
                <label><span>{t('autonomyPolicy.unit')}</span>
                  <input value={limit.unit} maxLength={POLICY_UNIT_MAX}
                    onChange={event => editLimit(index, { unit: event.target.value })} /></label>
                <label><span>{t('autonomyPolicy.minimum')}</span>
                  <input inputMode="decimal" value={limit.minimumValue}
                    onChange={event => editLimit(index, { minimumValue: event.target.value })} /></label>
                <label><span>{t('autonomyPolicy.maximum')}</span>
                  <input inputMode="decimal" value={limit.maximumValue}
                    onChange={event => editLimit(index, { maximumValue: event.target.value })} /></label>
                <ActionButton label={t('autonomyPolicy.removeCommand', { row: index + 1 })}
                  onClick={() => edit({ limits: draft.limits.filter((_, at) => at !== index) })} />
              </div>
            ))}
            <ActionButton label={t('autonomyPolicy.addCommand')} disabled={draft.limits.length >= POLICY_MAX_LIMITS}
              onClick={() => edit({ limits: [...draft.limits, { command: '', unit: '', minimumValue: '', maximumValue: '' }] })} />
          </fieldset>
          <label><span>{t('autonomyPolicy.maxActions', { max: POLICY_ACTIONS_MAX })}</span>
            <input inputMode="numeric" value={draft.maxActionsPerWindow} disabled={saving}
              onChange={event => edit({ maxActionsPerWindow: event.target.value })} /></label>
          <label><span>{t('autonomyPolicy.windowSeconds', { max: POLICY_WINDOW_MAX_SECONDS })}</span>
            <input inputMode="numeric" value={draft.windowSeconds} disabled={saving}
              onChange={event => edit({ windowSeconds: event.target.value })} /></label>
          <label className="recommendation-review-confirm">
            <input type="checkbox" checked={draft.allowApprovedRecommendations} disabled={saving}
              onChange={event => edit({ allowApprovedRecommendations: event.target.checked })} />
            {t('autonomyPolicy.approvedLabel')}
          </label>
          {issues.length > 0 && (
            <ul className="execution-detail-error" role="alert">
              {issues.map((issue, at) => <li key={at}>{policyIssueMessage(issue)}</li>)}
            </ul>
          )}
          {saveError && <p className="execution-detail-error" role="alert">{errors[saveError]}</p>}
          {saving && <p role="status">{t('autonomyPolicy.saving')}</p>}
          <div className="execution-detail-actions">
            <ActionButton label={t('decision.cancel')} onClick={close} disabled={saving} />
            <ActionButton variant="primary" label={t('autonomyPolicy.save')} disabled={blocked}
              onClick={() => void submit()} />
          </div>
        </form>
      )}
    </section>
  );
}
