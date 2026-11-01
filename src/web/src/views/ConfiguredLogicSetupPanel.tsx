import { useEffect, useMemo, useRef, useState } from 'react';
import { RefreshCw } from 'lucide-react';
import { ActionButton } from '../components/ActionButton';
import type { FormEvent } from 'react';
import type { AssetResponse, AssetTypeResponse, ConfiguredLogicRevisionResponse,
  LogicModuleResponse } from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { loadViews, loadViewSelection, loadViewMembers, VIEW_PAGE_SIZE } from './assetViewsApi';
import type { AssetView } from './assetViewsApi';
import { loadConnections } from './connections';
import type { Connection } from './connections';
import { eligibleAssets, isJsonObject, prefillBindings, readConfiguration, setupManifest,
  setupResponse, SetupApiError, verifiedConfiguration, matchesSetupRevision } from './configuredLogicSetup';
import type { SetupRequest, VerifiedConfiguration } from './configuredLogicSetup';
import { parameterFields, parameterValues, buildParameters } from './moduleParameterForm';
import type { AttributeFieldError, AttributeValues } from './assetAttributeForm';
import { ParameterOverridesEditor } from './ParameterOverridesEditor';
import { AttributeFieldsEditor } from '../components/AttributeFieldsEditor';
import './configuredLogicSetup.css';
import { LoadingIndicator } from '../components/LoadingIndicator';

type Props = {
  modules: LogicModuleResponse[]; assets: AssetResponse[]; assetTypes: AssetTypeResponse[];
  configuration?: VerifiedConfiguration; revision?: ConfiguredLogicRevisionResponse; editDraft?: boolean;
  onSaved: (configuration: VerifiedConfiguration, savedRevisionId?: string) => void; onCancel: () => void;
  onDenied?: (committed: boolean) => void;
  /** Begin from this asset: it is pre-selected for the first role it fits. */
  initialAssetId?: string | null; canReadViews?: boolean;
};
export function ConfiguredLogicSetupPanel({ modules, assets, assetTypes,
  configuration, revision, editDraft = false, onSaved, onCancel, onDenied, initialAssetId, canReadViews = false }: Props) {
  const [views, setViews] = useState<AssetView[]>([]);
  const [connections, setConnections] = useState<Connection[]>([]);
  const [viewAssets, setViewAssets] = useState<AssetResponse[]>([]);
  const [viewRef, setViewRef] = useState<{ viewId: string; expectedRevision: number; role: string } | null>(() =>
    revision?.view ? { viewId: revision.view.viewId ?? '', expectedRevision: revision.view.revision ?? 0, role: revision.view.role ?? '' } : null);
  const [viewLoading, setViewLoading] = useState(false);
  const [viewError, setViewError] = useState(false);
  const [detailLimit, setDetailLimit] = useState(20);
  const viewController = useRef(new AbortController());
  const allAssets = useMemo(() => [...new Map([...assets, ...viewAssets].map(asset => [asset.id, asset])).values()], [assets, viewAssets]);
  useEffect(() => {
    const request = new AbortController();
    if (canReadViews) Promise.all([loadViews(request.signal), loadConnections(request.signal)]).then(([items, sources]) => {
      if (!request.signal.aborted) { setViews(items); setConnections(sources); }
    }).catch(() => { if (!request.signal.aborted) setViewError(true); });
    return () => { request.abort(); viewController.current.abort(); };
  }, [canReadViews]);
  const approved = modules.filter(m => setupManifest(m));
  const [moduleKey, setModuleKey] = useState(() => revision
    ? JSON.stringify([revision.moduleId, revision.moduleVersion]) : '');
  const module = approved.find(m => JSON.stringify([m.moduleId, m.version]) === moduleKey);
  const manifest = module ? setupManifest(module) : null;
  const [name, setName] = useState(configuration?.name ?? '');
  const [parameters, setParameters] = useState(revision?.parametersJson ?? '{}');
  // Modules that describe their settings get typed fields; anything the form cannot show stays JSON.
  const paramFields = useMemo(() => manifest?.parameterSchema && typeof manifest.parameterSchema === 'object'
    ? parameterFields(manifest.parameterSchema) : null, [manifest]);
  const [paramValues, setParamValues] = useState<AttributeValues | null>(null);
  const [overrideValidity, setOverrideValidity] = useState<Record<string, boolean>>({});
  const [paramErrors, setParamErrors] = useState<Record<string, AttributeFieldError>>({});
  const [paramJson, setParamJson] = useState(false);
  const savedValues = useMemo(() => paramFields && revision ? parameterValues(paramFields, revision.parametersJson ?? '{}') : {},
    [paramFields, revision]);
  const formValues = paramValues ?? savedValues ?? {};
  const settingsForm = paramFields !== null && !paramJson && (paramValues !== null || savedValues !== null);
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

  async function chooseView(id: string, role: string) {
    viewController.current.abort();
    const request = new AbortController(); viewController.current = request;
    setViewError(false);
    if (!id) { setViewRef(null); setViewAssets([]); setBindings(current => ({ ...current, [role]: [] })); setViewLoading(false); return; }
    const chosen = views.find(view => view.id === id);
    if (!chosen) return;
    setViewLoading(true);
    try {
      const ids = await loadViewSelection(chosen, request.signal);
      const members: AssetResponse[] = [];
      for (let offset = 0; offset < ids.length; offset += VIEW_PAGE_SIZE) {
        const page = await loadViewMembers(id, offset, request.signal);
        if (page.length === 0) throw new Error('Incomplete View membership');
        members.push(...page);
      }
      if (request.signal.aborted) return;
      const selectedIds = new Set(ids);
      if (members.length !== ids.length || new Set(members.map(member => member.id)).size !== ids.length ||
        members.some(member => !selectedIds.has(member.id ?? '') || member.ownerConnectionId !== chosen.connectionId)) throw new Error('Changed View membership');
      setViewAssets(members); setBindings(current => ({ ...current, [role]: ids }));
      setViewRef({ viewId: id, expectedRevision: chosen.revision, role }); setData({}); setOverrides({}); setOverrideValidity({}); setDetailLimit(20);
    } catch { if (!request.signal.aborted) setViewError(true); }
    finally { if (!request.signal.aborted) setViewLoading(false); }
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!manifest || !module?.manifestJson || pending.current || blocked || viewLoading || (viewRef && !canReadViews)) return;
    if (manifest.assetRequirements.some(r => (bindings[r.role] ?? []).some(id => overrideValidity[JSON.stringify([r.role, id])] === false))) { setError(t('assets.createFieldsInvalid')); return; }
    const assetBindings = manifest.assetRequirements.flatMap(r =>
      (bindings[r.role] ?? []).map(assetId => ({ role: r.role, assetId,
        parameterOverridesJson: overrides[JSON.stringify([r.role, assetId])] ?? '{}' })));
    const dataBindings = manifest.dataRequirements.flatMap(r =>
      (bindings[r.assetRole] ?? []).map(assetId => ({ requirementId: r.id, assetId,
        ...(data[JSON.stringify([r.id, assetId])] ?? { providerId: '', providerAssetExternalId: '', providerMappingJson: '{}' }) })));
    let parametersJson = parameters;
    if (settingsForm && paramFields) {
      const built = buildParameters(paramFields, formValues);
      if (Object.keys(built.errors).length > 0) { setParamErrors(built.errors); setError(t('assets.createFieldsInvalid')); return; }
      parametersJson = built.json;
    }
    const valid = name.trim() && isJsonObject(parametersJson) &&
      manifest.assetRequirements.every(r => {
        const ids = bindings[r.role] ?? [];
        const eligibleIds = new Set(eligibleAssets(r, allAssets, assetTypes).map(asset => asset.id));
        return ids.length >= r.minimumCount && ids.length <= r.maximumCount &&
          (viewRef?.role === r.role || ids.every(id => eligibleIds.has(id)));
      }) && assetBindings.length > 0 && assetBindings.every(b => isJsonObject(b.parameterOverridesJson)) &&
      dataBindings.every(b => b.providerId.trim() && b.providerAssetExternalId.trim() && isJsonObject(b.providerMappingJson)) &&
      (!useSchedule || manifest.executionProfile === 'ScheduledOneShot' &&
        Number.isSafeInteger(Number(cadence)) && Number(cadence) > 0 &&
        /Z$|[+-]00:00$/.test(start) && Number.isFinite(Date.parse(start)) && zone.trim());
    if (!valid) { setError(t('setup.formInvalid')); return; }
    const body: SetupRequest = { name: name.trim(), moduleManifestJson: module.manifestJson,
      parametersJson, assetBindings, dataBindings,
      schedule: useSchedule ? { enabled: scheduleEnabled, cadenceSeconds: Number(cadence),
        startAtUtc: new Date(start).toISOString(), timeZoneId: zone.trim() } : undefined,
      view: viewRef ?? undefined };
    pending.current = true; setBusy(true); setError(null);
    const request = new AbortController(); controller.current = request;
    let committed = false;
    try {
      const response = await fetch(configuration
        ? '/api/v1/configured-logic/' + configuration.id + '/revisions' + (editDraft ? '/' + revision?.id : '') : '/api/v1/configured-logic',
      { method: editDraft ? 'PUT' : 'POST', credentials: 'same-origin', signal: request.signal,
        headers: { 'Content-Type': 'application/json', ...(editDraft ? { 'If-Match': '"' + (revision?.draftVersion ?? 0) + '"' } : {}) }, body: JSON.stringify(body) });
      const value = await setupResponse(response, editDraft ? 200 : 201, true);
      committed = true;
      const receipt = configuration ? value as ConfiguredLogicRevisionResponse : null;
      const id = configuration?.id ?? (verifiedConfiguration(value) ? value.id : null);
      if (!id) throw new SetupApiError('uncertain');
      const saved = await readConfiguration(id, request.signal);
      const found = configuration ? saved.revisions.find(r => r.id === receipt?.id) : saved.revisions[0];
      if (editDraft && (receipt?.id !== revision?.id || receipt?.draftVersion !== (revision?.draftVersion ?? 0) + 1 ||
        found?.draftVersion !== receipt.draftVersion)) throw new SetupApiError('conflict');
      if (!found || found.status !== 'Draft' || found.moduleId !== manifest.moduleId ||
        found.moduleVersion !== manifest.version || saved.name !== name.trim() ||
        !matchesSetupRevision(found, body))
        throw new SetupApiError('uncertain');
      if (!request.signal.aborted) { if (editDraft) onSaved(saved, found.id); else onSaved(saved); }
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
    <h3>{editDraft ? t('draftEditor.edit', { number: revision?.revisionNumber ?? '—' }) : configuration ? t('setup.newRevision') : t('setup.heading')}</h3>
    {configuration && <p>{t(editDraft ? 'draftEditor.editHint' : 'draftEditor.copyHint', { number: revision?.revisionNumber ?? '—' })}</p>}
    <p>{t('setup.shadowOnly')}</p>
    {initialAssetId && !configuration && <p role="note">{t('setup.startingFromAsset', { name: allAssets.find(a => a.id === initialAssetId)?.name ?? initialAssetId })}</p>}
    {approved.length === 0 && <p role="status">{t('setup.noModules')}</p>}
    <form className={configuration ? 'setup-form revision-editor' : 'setup-form'} onSubmit={e => void submit(e)}>
      <fieldset disabled={busy || blocked}>
        <h4 className="setup-step">{t('setupStep.logic')}</h4>
        <p className="setup-step-hint">{t('setupStep.logicHint')}</p>
        <label>{t('setup.module')}<select required value={moduleKey} onChange={e => {
          const chosen = approved.find(m => JSON.stringify([m.moduleId, m.version]) === e.target.value);
          const chosenManifest = chosen ? setupManifest(chosen) : null;
          viewController.current.abort(); setViewRef(null); setViewAssets([]); setViewLoading(false); setViewError(false);
          setModuleKey(e.target.value);
          setBindings(chosenManifest && !configuration ? prefillBindings(chosenManifest, initialAssetId, assets, assetTypes) : {});
          setData({}); setOverrides({}); setOverrideValidity({}); setUseSchedule(false);
          setParamValues(null); setParamErrors({}); setParamJson(false); setParameters('{}');
        }}><option value="">{t('setup.choose')}</option>{approved.map(m =>
          <option key={m.id} value={JSON.stringify([m.moduleId, m.version])}>
            {m.displayName} · {m.version}</option>)}</select></label>
        <h4 className="setup-step">{t('setupStep.name')}</h4>
        <p className="setup-step-hint">{t('setupStep.nameHint')}</p>
        <label>{t('setup.name')}<input required maxLength={256} value={name}
          disabled={!!configuration} onChange={e => setName(e.target.value)} /></label>
        <details className="revision-editor-section" open><summary>{t('draftEditor.settings')}</summary>

        {manifest?.description && <p>{t('setup.moduleDescription')}: {manifest.description}</p>}
        <p className="setup-step-hint">{t(settingsForm ? 'setupStep.settingsFormHint' : 'setupStep.settingsHint')}</p>
        {settingsForm && paramFields ? <AttributeFieldsEditor jsonLabel={t('draftEditor.editJson')} fields={paramFields} values={formValues}
          errors={paramErrors} idPrefix="setup-param-" legend={t('setup.settingsLegend')}
          onChange={(key, value) => {
            setParamValues({ ...formValues, [key]: value });
            setParamErrors(current => Object.fromEntries(Object.entries(current).filter(([k]) => k !== key)));
            setError(null);
          }}
          onUseJson={() => { setParameters(buildParameters(paramFields, formValues).json); setParamJson(true); setParamErrors({}); }} />
        : <><label>{t('setup.parameters')}<textarea required rows={4} maxLength={65536}
          value={parameters} onChange={e => setParameters(e.target.value)} /></label>
        <p>{t(manifest?.parameterSchema ? 'setup.parametersHintSchema' : 'setup.parametersHint')}</p>
        {paramFields && parameterValues(paramFields, parameters) !== null && <ActionButton label={t('draftEditor.useFields')}
          onClick={() => { setParamValues(parameterValues(paramFields, parameters)); setParamJson(false); setParamErrors({}); }} />}</>}
        </details>
        <details className="revision-editor-section" open><summary>{t('draftEditor.targets')}</summary>
        {manifest && <>
        <p className="setup-step-hint">{t('setupStep.assetsHint')}</p></>}
        {manifest?.assetRequirements.map(r => <fieldset key={r.role}>
          <legend>{t('setup.assetRole', { role: r.role, min: r.minimumCount, max: r.maximumCount })}</legend>
          {canReadViews && <label>{t('setup.targetView')}<select value={viewRef?.role === r.role ? viewRef.viewId : ''} disabled={viewLoading}
            onChange={event => void chooseView(event.target.value, r.role)}>
            <option value="">{t('setup.individualAssets')}</option>
            {views.map(view => {
              const source = connections.find(connection => connection.id === view.connectionId);
              const incompatible = view.hasUnavailableMembers || view.memberCount < r.minimumCount || view.memberCount > r.maximumCount ||
                !source?.isEnabled || (!!manifest.requiredAdapterKey && manifest.requiredAdapterKey.toLowerCase() !== source.adapterKey.toLowerCase());
              return <option key={view.id} value={view.id} disabled={incompatible}>{view.name} · {view.memberCount} · {source?.name ?? view.connectionId}</option>;
            })}
          </select></label>}
          {canReadViews && <p className="connection-help">{t('setup.viewCompatibility')}</p>}
          {viewRef?.role === r.role && <p className="connection-help">{t('setup.viewSnapshot', { revision: viewRef.expectedRevision, count: bindings[r.role]?.length ?? 0 })}
            {canReadViews && <ActionButton icon={RefreshCw} iconOnly label={t('setup.refreshView')} disabled={viewLoading} onClick={() => void chooseView(viewRef.viewId, r.role)} />}</p>}
          {viewLoading && <LoadingIndicator label={t('views.loading')} />}
          {viewError && <p role="alert">{t('setup.viewUnavailable')}</p>}
          <p>{t(viewRef?.role === r.role ? 'setup.viewTargets' : 'setup.loadedAssets')}</p>
          {manifest.requiredAdapterKey === 'foresite' && !viewRef && <p className="connection-help">{t('setup.directWellHint')}</p>}
          {!viewRef && (bindings[r.role] ?? []).map(id => {
            const owner = allAssets.find(asset => asset.id === id)?.ownerConnectionId;
            return owner ? <p key={id} className="connection-help">{t('setup.selectedConnection', {
              connection: connections.find(connection => connection.id === owner)?.name ?? owner })}</p> : null;
          })}
          <label>{t('setup.assetsFor', { role: r.role })}<select multiple disabled={viewRef?.role === r.role || viewLoading}
            value={bindings[r.role] ?? []} onChange={e => setBindings({ ...bindings,
              [r.role]: Array.from(e.target.selectedOptions, o => o.value) })}>
            {eligibleAssets(r, allAssets, assetTypes).filter(a => !manifest.requiredAdapterKey || !!a.ownerConnectionId)
              .map(a => <option key={a.id} value={a.id}>{a.name} · {a.id}</option>)}
          </select></label>
          {(bindings[r.role] ?? []).slice(0, detailLimit).map(id => <ParameterOverridesEditor
            key={moduleKey + JSON.stringify([r.role, id])} schema={manifest.parameterSchema}
            context={allAssets.find(a => a.id === id)?.name ?? id}
            label={t('setup.overrides', { asset: allAssets.find(a => a.id === id)?.name ?? id })}
            idPrefix={'override-' + r.role + '-' + id + '-'} value={overrides[JSON.stringify([r.role, id])] ?? '{}'}
            onValidity={valid => setOverrideValidity(current => ({ ...current, [JSON.stringify([r.role, id])]: valid }))}
            onChange={value => setOverrides(current => ({ ...current, [JSON.stringify([r.role, id])]: value }))} />)}
        </fieldset>)}
        </details>
        <details className="revision-editor-section" open><summary>{t('draftEditor.bindings')}</summary>
        {manifest && manifest.dataRequirements.length > 0 && <>
        <p className="setup-step-hint">{t('setupStep.dataHint')}</p></>}
        {manifest?.dataRequirements.flatMap(r => (bindings[r.assetRole] ?? []).slice(0, detailLimit).map(id => {
          const key = JSON.stringify([r.id, id]);
          const binding = data[key] ?? { providerId: '', providerAssetExternalId: '', providerMappingJson: '{}' };
          return <fieldset key={key}><legend>{r.id} · {r.quantity} · {allAssets.find(a => a.id === id)?.name ?? id}</legend>
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
        </details>
        {manifest?.executionProfile === 'ScheduledOneShot' && <h4 className="setup-step">{t('setupStep.schedule')}</h4>}
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
        {Object.values(bindings).some(ids => ids.length > detailLimit) && <ActionButton label={t('setup.moreTargets')} onClick={() => setDetailLimit(limit => limit + 20)} />}
        <ActionButton type="submit" variant="primary" label={busy ? t('setup.saving') : editDraft ? t('draftEditor.save') : t('setup.save')} disabled={!manifest || approved.length === 0 || viewLoading || (!!viewRef && !canReadViews)} />
      </fieldset>
      {error && <p role="alert">{error}</p>}
      <ActionButton label={t('setup.cancel')} disabled={busy} onClick={onCancel} />
    </form>
  </section>;
}
