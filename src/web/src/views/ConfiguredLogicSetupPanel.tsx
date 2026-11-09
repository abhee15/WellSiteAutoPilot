import { useEffect, useMemo, useRef, useState } from 'react';
import { RefreshCw } from 'lucide-react';
import { ActionButton } from '../components/ActionButton';
import { FormActions, SelectField, TextAreaField, TextField } from '../components/FormFields';
import { Notice } from '../components/Notice';
import type { FormEvent } from 'react';
import type { AssetResponse, AssetTypeResponse, ConfiguredLogicRevisionResponse,
  LogicModuleResponse } from '../api/generated/v1/types.gen';
import { AssignmentScheduleEditor } from './AssignmentScheduleEditor';
import { initialScheduleDraft, buildAssignmentSchedule, scheduleErrorKeys } from './assignmentSchedule';
import { resolveAssignmentBindings } from './assignmentBindings';
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
import { CollapsibleSection, Section } from '../components/Section';
import './configuredLogicSetup.css';
import { LoadingIndicator } from '../components/LoadingIndicator';
import type { OwnerGroup } from './assetHierarchy';
import { assetIdentityLabel } from './runContext';
import { useUnsavedChangesGuard } from '../navigationGuard';

/** The module picker is excluded from the unsaved-changes signal: picking one fills in defaults rather than entering work. */
const MODULE_FIELD_ID = 'setup-module';

type Props = {
  modules: LogicModuleResponse[]; assets: AssetResponse[]; assetTypes: AssetTypeResponse[]; owners?: readonly OwnerGroup[];
  configuration?: VerifiedConfiguration; revision?: ConfiguredLogicRevisionResponse; editDraft?: boolean;
  onSaved: (configuration: VerifiedConfiguration, savedRevisionId?: string) => void; onCancel: () => void;
  onDenied?: (committed: boolean) => void;
  /** Begin from this asset: it is pre-selected for the first role it fits. */
  initialAssetId?: string | null; initialViewId?: string | null; canReadViews?: boolean;
  showHeading?: boolean;
};
export function ConfiguredLogicSetupPanel({ modules, assets, assetTypes, owners = [],
  configuration, revision, editDraft = false, onSaved, onCancel, onDenied, initialAssetId, initialViewId, canReadViews = false, showHeading = true }: Props) {
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
  const labelAsset = (id?: string | null) => assetIdentityLabel(id, allAssets, owners);
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
  const bindingKey = JSON.stringify([moduleKey, bindings]);
  const [bindingResolution, setBindingResolution] = useState<{ key: string; error: string | null } | null>(null);
  const originalBindingKey = useRef(bindingKey);
  const hasPreservedBindings = Boolean(revision && bindingKey === originalBindingKey.current);
  const initialViewApplied = useRef(false);
  useEffect(() => {
    if (!initialViewId || !manifest || !views.length || initialViewApplied.current) return;
    initialViewApplied.current = true;
    const role = manifest.assetRequirements.find(item => item.maximumCount > 0)?.role;
    if (role) void chooseView(initialViewId, role);
  });
  useEffect(() => {
    if (!manifest || !manifest.dataRequirements.length || hasPreservedBindings) return;
    const request = new AbortController();
    const capturedKey = bindingKey;
    const capturedTargets = JSON.parse(capturedKey)[1] as Record<string, string[]>;
    void resolveAssignmentBindings(module?.manifestJson ?? '', capturedTargets, request.signal).then(rows => {
      if (request.signal.aborted) return;
      setData(Object.fromEntries(rows.map(row => [JSON.stringify([row.requirementId, row.assetId]), row])));
      setBindingResolution({ key: capturedKey, error: null });
    }).catch(() => {
      if (!request.signal.aborted) setBindingResolution({ key: capturedKey,
        error: t('assignment.mappingError') });
    });
    return () => request.abort();
    // The key contains the module version and all selected asset-role bindings.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [bindingKey, hasPreservedBindings]);
  const mappingPending = Boolean(manifest?.dataRequirements.length && !hasPreservedBindings &&
    bindingResolution?.key !== bindingKey);
  const mappingError = !hasPreservedBindings && bindingResolution?.key === bindingKey ? bindingResolution.error : null;
  const [scheduleDraft, setScheduleDraft] = useState(() => initialScheduleDraft(revision?.schedule));
  const scheduleResult = buildAssignmentSchedule(scheduleDraft);
  // This form's state is also changed by the form itself (choosing a module fills the name and bindings, choosing a
  // view fills the assets), so "differs from a snapshot" would prompt on a form nobody has touched. Unsaved here means
  // the person has changed a field: a real change event from inside the form, other than the module picker.
  const [edited, setEdited] = useState(false);
  const markSaved = useUnsavedChangesGuard(edited);
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
    if (mappingPending || mappingError) { setError(mappingError ?? t('assignment.resolving')); return; }
    if (!manifest || !module?.manifestJson || pending.current || blocked || viewLoading || (viewRef && !canReadViews)) return;
    if (manifest.assetRequirements.some(r => (bindings[r.role] ?? []).some(id => overrideValidity[JSON.stringify([r.role, id])] === false))) { setError(t('assets.createFieldsInvalid')); return; }
    const assetBindings = manifest.assetRequirements.flatMap(r =>
      (bindings[r.role] ?? []).map(assetId => ({ role: r.role, assetId,
        parameterOverridesJson: overrides[JSON.stringify([r.role, assetId])] ?? '{}' })));
    const resolvedData = hasPreservedBindings ? Object.fromEntries((revision?.dataBindings ?? []).map(binding =>
      [JSON.stringify([binding.requirementId, binding.assetId]), {
        providerId: binding.providerId ?? '', providerAssetExternalId: binding.providerAssetExternalId ?? '',
        providerMappingJson: binding.providerMappingJson ?? '{}',
      }])) : data;
    const dataBindings = manifest.dataRequirements.flatMap(r =>
      (bindings[r.assetRole] ?? []).map(assetId => ({ requirementId: r.id, assetId,
        ...(resolvedData[JSON.stringify([r.id, assetId])] ?? { providerId: '', providerAssetExternalId: '', providerMappingJson: '{}' }) })));
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
      !scheduleResult.error;
    if (!valid) { setError(scheduleResult.error ? t(scheduleErrorKeys[scheduleResult.error]) : t('assignment.reviewFields')); return; }
    const body: SetupRequest = { name: name.trim(), moduleManifestJson: module.manifestJson,
      parametersJson, assetBindings, dataBindings,
      schedule: scheduleResult.schedule ?? undefined,
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
      if (!request.signal.aborted) { markSaved(); if (editDraft) onSaved(saved, found.id); else onSaved(saved); }
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
    {showHeading && <h3>{editDraft ? t('draftEditor.edit', { number: revision?.revisionNumber ?? '—' }) : configuration ? t('setup.newRevision') : t('setup.heading')}</h3>}
    {configuration && <p>{t(editDraft ? 'draftEditor.editHint' : 'draftEditor.copyHint', { number: revision?.revisionNumber ?? '—' })}</p>}
    <p>{t('setup.shadowOnly')}</p>
    {initialViewId && !configuration && <Notice announce={false}>{t('assignment.selectedView', { name: views.find(view => view.id === initialViewId)?.name ?? initialViewId })}</Notice>}
    {initialAssetId && !configuration && <Notice announce={false}>{t('setup.startingFromAsset', { name: allAssets.find(a => a.id === initialAssetId)?.name ?? initialAssetId })}</Notice>}
    {approved.length === 0 && <Notice tone="warning">{t('setup.noModules')}</Notice>}
    <form className={configuration ? 'setup-form revision-editor' : 'setup-form'} onSubmit={e => void submit(e)}
      onChange={event => { if ((event.target as HTMLElement).id !== MODULE_FIELD_ID) setEdited(true); }}>
      <fieldset disabled={busy || blocked}>
        <Section title={t('setupStep.logic')} hint={t('setupStep.logicHint')}>
        <SelectField id={MODULE_FIELD_ID} label={t('setup.module')} required value={moduleKey} onChange={e => {
          const chosen = approved.find(m => JSON.stringify([m.moduleId, m.version]) === e.target.value);
          const chosenManifest = chosen ? setupManifest(chosen) : null;
          viewController.current.abort(); setViewRef(null); setViewAssets([]); setViewLoading(false); setViewError(false);
          initialViewApplied.current = false;
          setModuleKey(e.target.value);
          if (!configuration && !name.trim()) setName(chosen?.displayName ?? chosen?.moduleId ?? '');
          setBindings(chosenManifest && !configuration ? prefillBindings(chosenManifest, initialAssetId, assets, assetTypes) : {});
          setData({}); setOverrides({}); setOverrideValidity({}); setScheduleDraft(initialScheduleDraft());
          setParamValues(null); setParamErrors({}); setParamJson(false); setParameters('{}');
        }}><option value="">{t('setup.choose')}</option>{approved.map(m => {
          const candidate = setupManifest(m);
          const selectedView = views.find(view => view.id === (viewRef?.viewId ?? initialViewId));
          const source = connections.find(connection => connection.id === selectedView?.connectionId);
          const mismatch = Boolean(candidate?.requiredAdapterKey && source &&
            candidate.requiredAdapterKey.toLowerCase() !== source.adapterKey.toLowerCase());
          return <option key={m.id} value={JSON.stringify([m.moduleId, m.version])} disabled={mismatch}>
            {m.displayName} · {m.version}{mismatch ? ' · ' + t('assignment.requiresAdapter', { adapter: candidate?.requiredAdapterKey ?? '' }) : ''}</option>;
        })}</SelectField>
        </Section>
        <Section title={t('setupStep.name')} hint={t('setupStep.nameHint')}>
        <TextField label={t('setup.name')} required maxLength={256} value={name}
          disabled={!!configuration} onChange={e => setName(e.target.value)} />
        </Section>
        <CollapsibleSection title={t('draftEditor.settings')} hint={t(settingsForm ? 'setupStep.settingsFormHint' : 'setupStep.settingsHint')}
          open={Boolean(manifest?.parameterSchema || (revision?.parametersJson && revision.parametersJson !== '{}'))}>
        {manifest?.description && <p>{t('setup.moduleDescription')}: {manifest.description}</p>}
        {settingsForm && paramFields ? <AttributeFieldsEditor jsonLabel={t('draftEditor.editJson')} fields={paramFields} values={formValues}
          errors={paramErrors} idPrefix="setup-param-" legend={t('setup.settingsLegend')}
          onChange={(key, value) => {
            setParamValues({ ...formValues, [key]: value });
            setParamErrors(current => Object.fromEntries(Object.entries(current).filter(([k]) => k !== key)));
            setError(null);
          }}
          onUseJson={() => { setParameters(buildParameters(paramFields, formValues).json); setParamJson(true); setParamErrors({}); }} />
        : <><TextAreaField label={t('setup.parameters')} required rows={4} maxLength={65536}
          value={parameters} onChange={e => setParameters(e.target.value)} />
        <p>{t(manifest?.parameterSchema ? 'setup.parametersHintSchema' : 'setup.parametersHint')}</p>
        {paramFields && parameterValues(paramFields, parameters) !== null && <ActionButton label={t('draftEditor.useFields')}
          onClick={() => { setParamValues(parameterValues(paramFields, parameters)); setParamJson(false); setParamErrors({}); }} />}</>}
        </CollapsibleSection>
        <CollapsibleSection title={t('draftEditor.targets')} hint={manifest && !viewRef ? t('setupStep.assetsHint') : undefined} open>
        {manifest?.assetRequirements.map(r => <fieldset key={r.role}>
          <legend>{t('setup.assetRole', { role: r.role, min: r.minimumCount, max: r.maximumCount })}</legend>
          {canReadViews && <SelectField label={t('setup.targetView')} value={viewRef?.role === r.role ? viewRef.viewId : ''} disabled={viewLoading}
            onChange={event => void chooseView(event.target.value, r.role)}>
            <option value="">{t('setup.individualAssets')}</option>
            {views.map(view => {
              const source = connections.find(connection => connection.id === view.connectionId);
              const incompatible = view.hasUnavailableMembers || view.memberCount < r.minimumCount || view.memberCount > r.maximumCount ||
                !source?.isEnabled || (!!manifest.requiredAdapterKey && manifest.requiredAdapterKey.toLowerCase() !== source.adapterKey.toLowerCase());
              const reason = view.hasUnavailableMembers ? t('assignment.unavailableMembers')
                : !source?.isEnabled ? t('assignment.connectionDisabled')
                  : manifest.requiredAdapterKey && manifest.requiredAdapterKey.toLowerCase() !== source.adapterKey.toLowerCase()
                    ? t('assignment.requiresAdapter', { adapter: manifest.requiredAdapterKey })
                    : view.memberCount < r.minimumCount || view.memberCount > r.maximumCount
                      ? t('assignment.countRange', { min: r.minimumCount, max: r.maximumCount }) : '';
              return <option key={view.id} value={view.id} disabled={incompatible}>{view.name} · {view.memberCount} · {source?.name ?? view.connectionId}{reason ? ' · ' + reason : ''}</option>;
            })}
          </SelectField>}
          {canReadViews && !viewRef && <p className="connection-help">{t('setup.viewCompatibility')}</p>}
          {viewRef?.role === r.role && <p className="connection-help">{t('setup.viewSnapshot', { revision: viewRef.expectedRevision, count: bindings[r.role]?.length ?? 0 })}
            {canReadViews && <ActionButton icon={RefreshCw} iconOnly label={t('setup.refreshView')} disabled={viewLoading} onClick={() => void chooseView(viewRef.viewId, r.role)} />}</p>}
          {viewLoading && <LoadingIndicator size="small" label={t('views.loading')} />}
          {viewError && <Notice tone="error">{t('setup.viewUnavailable')}</Notice>}
          {viewRef?.role !== r.role && <p>{t('setup.loadedAssets')}</p>}
          {manifest.requiredAdapterKey === 'foresite' && !viewRef && <p className="connection-help">{t('setup.directWellHint')}</p>}
          {!viewRef && (bindings[r.role] ?? []).map(id => {
            const owner = allAssets.find(asset => asset.id === id)?.ownerConnectionId;
            return owner ? <p key={id} className="connection-help">{t('setup.selectedConnection', {
              connection: connections.find(connection => connection.id === owner)?.name ?? owner })}</p> : null;
          })}
          {viewRef?.role === r.role ? <CollapsibleSection title={t('setup.assetsFor', { role: r.role })} meta={bindings[r.role]?.length ?? 0}>
            <ul>{(bindings[r.role] ?? []).slice(0, detailLimit).map(id => <li key={id}>{labelAsset(id)}</li>)}</ul>
            <p className="connection-help">{t('setup.viewTargets')}</p>
          </CollapsibleSection> : <label>{t('setup.assetsFor', { role: r.role })}<select multiple disabled={viewLoading}
            value={bindings[r.role] ?? []} onChange={e => setBindings({ ...bindings,
              [r.role]: Array.from(e.target.selectedOptions, o => o.value) })}>
            {eligibleAssets(r, allAssets, assetTypes).filter(a => !manifest.requiredAdapterKey || !!a.ownerConnectionId)
              .map(a => <option key={a.id} value={a.id}>{labelAsset(a.id)}</option>)}
          </select></label>}
          {(bindings[r.role]?.length ?? 0) > 0 && <CollapsibleSection title={t('assignment.overrides')}>
          {(bindings[r.role] ?? []).slice(0, detailLimit).map(id => <ParameterOverridesEditor
            key={moduleKey + JSON.stringify([r.role, id])} schema={manifest.parameterSchema}
            context={labelAsset(id)}
            label={t('setup.overrides', { asset: labelAsset(id) })}
            idPrefix={'override-' + r.role + '-' + id + '-'} value={overrides[JSON.stringify([r.role, id])] ?? '{}'}
            onValidity={valid => setOverrideValidity(current => ({ ...current, [JSON.stringify([r.role, id])]: valid }))}
            onChange={value => setOverrides(current => ({ ...current, [JSON.stringify([r.role, id])]: value }))} />)}
          </CollapsibleSection>}
        </fieldset>)}
        </CollapsibleSection>
        {mappingPending && <LoadingIndicator size="small" label={t('assignment.resolving')} />}
        {mappingError && <Notice tone="error">{mappingError}</Notice>}
        <CollapsibleSection title={t('assignment.dataSources')}>
          <p>{t('assignment.derived')}</p>
          {manifest?.dataRequirements.map(input => <p key={input.id}>
            {input.quantity} · {(bindings[input.assetRole] ?? []).length} {t('assignment.wells')}
          </p>)}
        </CollapsibleSection>
        {manifest?.executionProfile === 'ScheduledOneShot' && <AssignmentScheduleEditor value={scheduleDraft} onChange={setScheduleDraft} />}
        {Object.values(bindings).some(ids => ids.length > detailLimit) && <ActionButton label={t('setup.moreTargets')} onClick={() => setDetailLimit(limit => limit + 20)} />}
        {manifest && !mappingPending && !mappingError && <section className="assignment-review" aria-label={t('assignment.review')}>
          <h4>{t('assignment.review')}</h4>
          <p>{module?.displayName} · {module?.version}</p>
          <p>{t('assignment.reviewCount', { count: new Set(Object.values(bindings).flat()).size })}</p>
          <p>{t('logicDetail.shadowMode')}</p>
          <p>{t(scheduleDraft.scheduled ? 'assignment.scheduled' : 'assignment.onDemand')}</p>
          {scheduleDraft.scheduled && <p>{scheduleDraft.every} {scheduleDraft.unit} · {scheduleDraft.date} {scheduleDraft.time} · {scheduleDraft.timeZone}</p>}
        </section>}
      </fieldset>
      {error && <Notice tone="error">{error}</Notice>}
      <FormActions>
        <ActionButton label={t('setup.cancel')} disabled={busy} onClick={onCancel} />
        <ActionButton type="submit" variant="primary" label={busy ? t('setup.saving') : editDraft ? t('draftEditor.save') : t('setup.save')} disabled={busy || blocked || !manifest || approved.length === 0 || Boolean(scheduleResult.error) || mappingPending || Boolean(mappingError) || viewLoading || (!!viewRef && !canReadViews)} />
      </FormActions>
    </form>
  </section>;
}
