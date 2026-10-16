import { useEffect, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import type { AssetResponse, AssetTypeResponse, ConfiguredLogicRevisionResponse,
  LogicModuleResponse } from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { eligibleAssets, isJsonObject, readConfiguration, setupManifest,
  setupResponse, SetupApiError, verifiedConfiguration, matchesSetupRevision } from './configuredLogicSetup';
import type { SetupRequest, VerifiedConfiguration } from './configuredLogicSetup';
import './configuredLogicSetup.css';

type Props = {
  modules: LogicModuleResponse[]; assets: AssetResponse[]; assetTypes: AssetTypeResponse[];
  configuration?: VerifiedConfiguration; revision?: ConfiguredLogicRevisionResponse;
  onSaved: (configuration: VerifiedConfiguration) => void; onCancel: () => void;
  onDenied?: (committed: boolean) => void;
};
export function ConfiguredLogicSetupPanel({ modules, assets, assetTypes,
  configuration, revision, onSaved, onCancel, onDenied }: Props) {
  const approved = modules.filter(m => setupManifest(m));
  const [moduleKey, setModuleKey] = useState(() => revision
    ? JSON.stringify([revision.moduleId, revision.moduleVersion]) : '');
  const module = approved.find(m => JSON.stringify([m.moduleId, m.version]) === moduleKey);
  const manifest = module ? setupManifest(module) : null;
  const [name, setName] = useState(configuration?.name ?? '');
  const [parameters, setParameters] = useState(revision?.parametersJson ?? '{}');
  const [bindings, setBindings] = useState<Record<string, string[]>>(() => {
    const result: Record<string, string[]> = {};
    for (const b of revision?.assetBindings ?? [])
      if (b.role && b.assetId) result[b.role] = [...(result[b.role] ?? []), b.assetId];
    return result;
  });
  const [overrides, setOverrides] = useState<Record<string, string>>(() =>
    Object.fromEntries((revision?.assetBindings ?? []).map(b =>
      [JSON.stringify([b.role, b.assetId]), b.parameterOverridesJson ?? '{}'])));
  const [data, setData] = useState<Record<string, {
    providerId: string; providerAssetExternalId: string; providerMappingJson: string;
  }>>(() => Object.fromEntries((revision?.dataBindings ?? []).map(b =>
    [JSON.stringify([b.requirementId, b.assetId]), { providerId: b.providerId ?? '',
      providerAssetExternalId: b.providerAssetExternalId ?? '', providerMappingJson: b.providerMappingJson ?? '{}' }])));
  const [useSchedule, setUseSchedule] = useState(!!revision?.schedule);
  const [scheduleEnabled, setScheduleEnabled] = useState(revision?.schedule?.enabled ?? false);
  const [cadence, setCadence] = useState(String(revision?.schedule?.cadenceSeconds ?? 60));
  const [start, setStart] = useState(revision?.schedule?.startAtUtc ?? '');
  const [zone, setZone] = useState(revision?.schedule?.timeZoneId ?? 'UTC');
  const [busy, setBusy] = useState(false);
  const [blocked, setBlocked] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const controller = useRef<AbortController | null>(null);
  const pending = useRef(false);
  useEffect(() => () => controller.current?.abort(), []);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!manifest || !module?.manifestJson || pending.current || blocked) return;
    const assetBindings = manifest.assetRequirements.flatMap(r =>
      (bindings[r.role] ?? []).map(assetId => ({ role: r.role, assetId,
        parameterOverridesJson: overrides[JSON.stringify([r.role, assetId])] ?? '{}' })));
    const dataBindings = manifest.dataRequirements.flatMap(r =>
      (bindings[r.assetRole] ?? []).map(assetId => ({ requirementId: r.id, assetId,
        ...(data[JSON.stringify([r.id, assetId])] ?? { providerId: '', providerAssetExternalId: '', providerMappingJson: '{}' }) })));
    const valid = name.trim() && isJsonObject(parameters) &&
      manifest.assetRequirements.every(r => {
        const ids = bindings[r.role] ?? [];
        return ids.length >= r.minimumCount && ids.length <= r.maximumCount &&
          ids.every(id => eligibleAssets(r, assets, assetTypes).some(a => a.id === id));
      }) && assetBindings.length > 0 && assetBindings.every(b => isJsonObject(b.parameterOverridesJson)) &&
      dataBindings.every(b => b.providerId.trim() && b.providerAssetExternalId.trim() && isJsonObject(b.providerMappingJson)) &&
      (!useSchedule || manifest.executionProfile === 'ScheduledOneShot' &&
        Number.isSafeInteger(Number(cadence)) && Number(cadence) > 0 &&
        /Z$|[+-]00:00$/.test(start) && Number.isFinite(Date.parse(start)) && zone.trim());
    if (!valid) { setError(t('setup.formInvalid')); return; }
    const body: SetupRequest = { name: name.trim(), moduleManifestJson: module.manifestJson,
      parametersJson: parameters, assetBindings, dataBindings,
      schedule: useSchedule ? { enabled: scheduleEnabled, cadenceSeconds: Number(cadence),
        startAtUtc: new Date(start).toISOString(), timeZoneId: zone.trim() } : undefined };
    pending.current = true; setBusy(true); setError(null);
    const request = new AbortController(); controller.current = request;
    let committed = false;
    try {
      const response = await fetch(configuration
        ? '/api/v1/configured-logic/' + configuration.id + '/revisions' : '/api/v1/configured-logic',
      { method: 'POST', credentials: 'same-origin', signal: request.signal,
        headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
      const value = await setupResponse(response, 201, true);
      committed = true;
      const receipt = configuration ? value as ConfiguredLogicRevisionResponse : null;
      const id = configuration?.id ?? (verifiedConfiguration(value) ? value.id : null);
      if (!id) throw new SetupApiError('uncertain');
      const saved = await readConfiguration(id, request.signal);
      const found = configuration ? saved.revisions.find(r => r.id === receipt?.id) : saved.revisions[0];
      if (!found || found.status !== 'Draft' || found.moduleId !== manifest.moduleId ||
        found.moduleVersion !== manifest.version || saved.name !== name.trim() ||
        !matchesSetupRevision(found, body))
        throw new SetupApiError('uncertain');
      if (!request.signal.aborted) onSaved(saved);
    } catch (reason) {
      if (request.signal.aborted) return;
      const e = reason instanceof SetupApiError ? reason : new SetupApiError('uncertain');
      if (e.kind === 'denied' && onDenied) { onDenied(committed); return; }
      setBlocked(committed || e.kind === 'uncertain' || e.kind === 'denied' || e.kind === 'conflict');
      setError(t(committed ? 'setup.committed' : `setup.${e.kind}`) + (e.code ? ` (${e.code})` : ''));
    } finally {
      pending.current = false;
      if (!request.signal.aborted) setBusy(false);
    }
  }
  return <section className="setup-panel" aria-label={t('setup.heading')}>
    <h3>{configuration ? t('setup.newRevision') : t('setup.heading')}</h3>
    <p>{t('setup.shadowOnly')}</p>
    {approved.length === 0 && <p role="status">{t('setup.noModules')}</p>}
    <form className="setup-form" onSubmit={e => void submit(e)}>
      <fieldset disabled={busy || blocked}>
        <label>{t('setup.name')}<input required maxLength={256} value={name}
          disabled={!!configuration} onChange={e => setName(e.target.value)} /></label>
        <label>{t('setup.module')}<select required value={moduleKey} onChange={e => {
          setModuleKey(e.target.value); setBindings({}); setData({}); setOverrides({}); setUseSchedule(false);
        }}><option value="">{t('setup.choose')}</option>{approved.map(m =>
          <option key={m.id} value={JSON.stringify([m.moduleId, m.version])}>
            {m.displayName} · {m.version}</option>)}</select></label>
        <label>{t('setup.parameters')}<textarea required rows={4} maxLength={65536}
          value={parameters} onChange={e => setParameters(e.target.value)} /></label>
        <p>{t('setup.parametersHint')}</p>
        {manifest?.assetRequirements.map(r => <fieldset key={r.role}>
          <legend>{t('setup.assetRole', { role: r.role, min: r.minimumCount, max: r.maximumCount })}</legend>
          <p>{t('setup.loadedAssets')}</p>
          <label>{t('setup.assetsFor', { role: r.role })}<select multiple
            value={bindings[r.role] ?? []} onChange={e => setBindings({ ...bindings,
              [r.role]: Array.from(e.target.selectedOptions, o => o.value) })}>
            {eligibleAssets(r, assets, assetTypes).map(a => <option key={a.id} value={a.id}>{a.name} · {a.id}</option>)}
          </select></label>
          {(bindings[r.role] ?? []).map(id => <label key={id}>
            {t('setup.overrides', { asset: assets.find(a => a.id === id)?.name ?? id })}
            <textarea rows={2} maxLength={65536} value={overrides[JSON.stringify([r.role, id])] ?? '{}'}
              onChange={e => setOverrides({ ...overrides, [JSON.stringify([r.role, id])]: e.target.value })} />
          </label>)}
        </fieldset>)}
        {manifest?.dataRequirements.flatMap(r => (bindings[r.assetRole] ?? []).map(id => {
          const key = JSON.stringify([r.id, id]);
          const binding = data[key] ?? { providerId: '', providerAssetExternalId: '', providerMappingJson: '{}' };
          return <fieldset key={key}><legend>{r.id} · {r.quantity} · {assets.find(a => a.id === id)?.name ?? id}</legend>
            {(['providerId', 'providerAssetExternalId', 'providerMappingJson'] as const).map(field =>
              <label key={field}>{t(`setup.${field}`)}
                {field === 'providerMappingJson'
                  ? <textarea required rows={2} maxLength={65536} value={binding[field]}
                    onChange={e => setData({ ...data, [key]: { ...binding, [field]: e.target.value } })} />
                  : <input required maxLength={field === 'providerAssetExternalId' ? 512 : 256} value={binding[field]}
                    onChange={e => setData({ ...data, [key]: { ...binding, [field]: e.target.value } })} />}
              </label>)}
          </fieldset>;
        }))}
        {manifest?.executionProfile === 'ScheduledOneShot' && <details>
          <summary>{t('setup.schedule')}</summary>
          <label><input type="checkbox" checked={useSchedule}
            onChange={e => setUseSchedule(e.target.checked)} />{t('setup.useSchedule')}</label>
          {useSchedule && <>
            <label><input type="checkbox" checked={scheduleEnabled}
              onChange={e => setScheduleEnabled(e.target.checked)} />{t('setup.scheduleEnabled')}</label>
            <p>{t('setup.scheduleWarning')}</p>
            <label>{t('setup.cadence')}<input required type="number" min={1} step={1}
              value={cadence} onChange={e => setCadence(e.target.value)} /></label>
            <label>{t('setup.start')}<input required value={start}
              placeholder="2026-10-16T12:00:00Z" onChange={e => setStart(e.target.value)} /></label>
            <label>{t('setup.zone')}<input required value={zone} onChange={e => setZone(e.target.value)} /></label>
          </>}
        </details>}
        <button type="submit" disabled={!manifest || approved.length === 0}>{busy ? t('setup.saving') : t('setup.save')}</button>
      </fieldset>
      {error && <p role="alert">{error}</p>}
      <button type="button" disabled={busy} onClick={onCancel}>{t('setup.cancel')}</button>
    </form>
  </section>;
}
