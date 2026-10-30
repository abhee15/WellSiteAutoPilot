import { RefreshCw } from 'lucide-react';
import { ActionButton } from '../components/ActionButton';
import { useEffect, useState } from 'react';
import { getLogicModulePackageState } from '../api/generated/v1/sdk.gen';
import type { LogicModuleResponse, LogicModulePackageStateResponse } from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { formatDetailTimestamp } from './executionDetail';
import { LoadingIndicator } from '../components/LoadingIndicator';

export function ModulePackageState({ module }: { module: LogicModuleResponse }) {
  const [state, setState] = useState<LogicModulePackageStateResponse | null>(null);
  const [failed, setFailed] = useState(false);
  const [refresh, setRefresh] = useState(0);
  useEffect(() => {
    const controller = new AbortController(); setState(null); setFailed(false);
    if (!module.moduleId || !module.version) return () => controller.abort();
    void getLogicModulePackageState({ path: { moduleId: module.moduleId, moduleVersion: module.version },
      credentials: 'same-origin', cache: 'no-store', signal: controller.signal }).then(result => {
      if (controller.signal.aborted) return;
      const value = result.data;
      if (result.response?.status !== 200 || !value ||
          !['isStored', 'publicationRecorded', 'objectAvailable', 'publicationPending', 'runtimeCompatible',
            'workerHealthy', 'gatewayHealthy', 'preflightReady'].every(key => typeof value[key as keyof LogicModulePackageStateResponse] === 'boolean') ||
          !Number.isSafeInteger(value.attemptCount) || (value.attemptCount ?? -1) < 0 ||
          !Number.isFinite(Date.parse(value.observedAtUtc ?? ''))) { setFailed(true); return; }
      setState(value);
    }).catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [module.moduleId, module.version, module.isEnabled, refresh]);
  useEffect(() => {
    if (!state?.publicationPending) return;
    const timer = window.setTimeout(() => setRefresh(value => value + 1), 5000);
    return () => window.clearTimeout(timer);
  }, [state]);
  const yesNo = (value: boolean | undefined) => t(value ? 'packageState.yes' : 'packageState.no');
  return <section aria-label={t('packageState.heading')}>
    <h4>{t('packageState.heading')}</h4>
    {!state && !failed && <LoadingIndicator label={t('packageState.loading')} />}
    {failed && <p role="alert">{t('packageState.failed')}</p>}
    {state && <>
      <p role="status">{t(state.preflightReady ? 'packageState.ready' : 'packageState.notReady')}</p>
      <dl className="execution-detail-grid">
        {[[t('packageState.stored'), yesNo(state.isStored)], [t('packageState.published'), yesNo(state.publicationRecorded)],
          [t('packageState.available'), yesNo(state.objectAvailable)], [t('packageState.worker'), yesNo(state.workerHealthy)],
          [t('packageState.gateway'), yesNo(state.gatewayHealthy)], [t('packageState.compatible'), yesNo(state.runtimeCompatible)],
          [t('packageState.observed'), formatDetailTimestamp(state.observedAtUtc)]].map(([label, value]) =>
          <div key={label}><dt>{label}</dt><dd>{value}</dd></div>)}
      </dl>
      {state.publicationPending && <p>{t('packageState.pending', { count: state.attemptCount ?? 0 })}</p>}
      {state.lastFailureCode && <p>{state.lastFailureCode} · {formatDetailTimestamp(state.nextAttemptAtUtc)}</p>}
      {state.compatibilityFailureCode && <p>{state.compatibilityFailureCode}</p>}
    </>}
    <p>{t('packageState.hint')}</p>
    <ActionButton icon={RefreshCw} iconOnly label={t('packageState.refresh')} onClick={() => setRefresh(value => value + 1)} />
  </section>;
}
