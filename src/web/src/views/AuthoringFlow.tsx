import { useEffect, useMemo, useState } from 'react';
import { Trash2 } from 'lucide-react';
import {
  createGuidedModuleDraft,
  editGuidedModuleDraft,
  submitGuidedModuleRules,
  getGuidedModuleDraft,
  listGuidedModuleProviders,
  listConnectionAdapters,
} from '../api/generated/v1/sdk.gen';
import type {
  AdapterCommandResponse,
  GuidedModuleDraftResponse,
  GuidedProviderResponse,
  GuidedSourceHintRequest,
} from '../api/generated/v1/types.gen';
import { ActionButton, ActionLink } from '../components/ActionButton';
import { NumberField, SelectField, TextField } from '../components/FormFields';
import { Notice } from '../components/Notice';
import { Section } from '../components/Section';
import { t } from '../localization';
import { hashFor } from '../routing';
import { loadAdapterOperations } from './adapterOperations';
import { CalculationsEditor, RulesEditor } from './AuthoringLogic';
import { GuidedLivePreview } from './GuidedLivePreview';
import { TestsEditor, parseScenarios, type Scenario } from './AuthoringTests';
import { ParametersEditor, hasParameterProblems } from './AuthoringParameters';
import { emptyRules, logicIncomplete, parseRules, serializeRules } from './rulesModel';
import type { RulesModel } from './rulesModel';
import {
  addCurrentInput,
  addDailyInput,
  inputName,
  dailyCapability,
  blankContractManifest,
  contractIsComplete,
  inputsOf,
  parseManifest,
  removeInput,
  setInputMaximumAge,
  sourceOptionsFrom,
} from './contractManifest';
import type { SourceOption } from './contractManifest';
import { GuidedSourceHintFields } from './GuidedSourceHintFields';
import './authoringFlow.css';

// Where a reading can come from. Each kind is added here once its readings can be listed and run.
const inputSources = ['current', 'daily'] as const;
const otherField = '__other';
const sourceLabels = { current: 'flow.sourceCurrent', daily: 'flow.sourceDaily' } as const;

const text = (value: unknown) => (typeof value === 'string' ? value : '');

// The server explains why it refused a save (an unknown name in an expression, say); show that, not a generic line.
const detailOf = (error: unknown) =>
  typeof error === 'object' && error !== null && typeof (error as { detail?: unknown }).detail === 'string'
    ? (error as { detail: string }).detail
    : '';

type Section = 'inputs' | 'parameters' | 'calculations' | 'rules' | 'tests';
const outline: {
  id: Section;
  label:
    | 'flow.outlineInputs'
    | 'flow.outlineParameters'
    | 'flow.outlineCalculations'
    | 'flow.outlineRules'
    | 'flow.outlineTests';
}[] = [
  { id: 'inputs', label: 'flow.outlineInputs' },
  { id: 'parameters', label: 'flow.outlineParameters' },
  { id: 'calculations', label: 'flow.outlineCalculations' },
  { id: 'rules', label: 'flow.outlineRules' },
  { id: 'tests', label: 'flow.outlineTests' },
];

/**
 * Module authoring workbench. A module is created with a name and a provider, then edited here: an outline of the
 * parts of the module, and the editor for the selected part. Parts are added to the outline as they can be run.
 */
export function AuthoringFlow({ draftId }: { draftId?: string }) {
  const [providers, setProviders] = useState<GuidedProviderResponse[]>([]);
  const [name, setName] = useState('');
  const [providerKey, setProviderKey] = useState('');
  const [manifest, setManifest] = useState('');
  const [hints, setHints] = useState<GuidedSourceHintRequest[]>([]);
  const [rules, setRules] = useState<RulesModel>(emptyRules);
  const [draft, setDraft] = useState<GuidedModuleDraftResponse | null>(null);
  const [dirty, setDirty] = useState(false);
  const [selected, setSelected] = useState<Section>('inputs');
  const [justSaved, setJustSaved] = useState(false);
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState<string | null>(null);
  const [scenarios, setScenarios] = useState<Scenario[]>([]);
  const [submitted, setSubmitted] = useState(false);
  const [scenariosChanged, setScenariosChanged] = useState(false);
  const [commands, setCommands] = useState<AdapterCommandResponse[]>([]);

  useEffect(() => {
    const controller = new AbortController();
    void listGuidedModuleProviders({ signal: controller.signal })
      .then(({ data }) => {
        if (!controller.signal.aborted && data) setProviders(data);
      })
      .catch(() => undefined);
    return () => controller.abort();
  }, []);

  useEffect(() => {
    if (!draftId) return;
    const controller = new AbortController();
    void getGuidedModuleDraft({ path: { id: draftId }, signal: controller.signal })
      .then(({ data }) => {
        if (controller.signal.aborted) return;
        if (!data?.manifestJson || data.definition) {
          setFailed(t('flow.cannotOpen'));
          return;
        }
        const loaded = parseManifest(data.manifestJson) ?? {};
        setName(text(loaded.displayName));
        setProviderKey(text(loaded.requiredAdapterKey));
        setManifest(data.manifestJson);
        setHints(data.sourceHints ?? []);
        setRules(parseRules(data.rulesJson));
        setScenarios(parseScenarios(data.scenariosJson));
        setDraft(data);
        setDirty(false);
      })
      .catch(() => {
        if (!controller.signal.aborted) setFailed(t('flow.cannotOpen'));
      });
    return () => controller.abort();
  }, [draftId]);

  useEffect(() => {
    if (!providerKey) return;
    const controller = new AbortController();
    void listConnectionAdapters({ signal: controller.signal })
      .then(({ data }) => {
        if (controller.signal.aborted) return;
        const adapter = data?.find((item) => item.key?.toLowerCase() === providerKey.toLowerCase());
        setCommands(adapter?.commands ?? []);
      })
      .catch(() => {
        if (!controller.signal.aborted) setCommands([]);
      });
    return () => controller.abort();
  }, [providerKey]);

  const provider = providers.find((item) => item.adapterKey === providerKey);
  const inputs = useMemo(() => (manifest ? inputsOf(manifest) : []), [manifest]);
  const inputNames = useMemo(() => inputs.map(inputName), [inputs]);
  const counts: Record<Section, number> = {
    inputs: inputs.length,
    parameters: rules.parameters.length,
    calculations: rules.calculations.length,
    rules: rules.rules.length,
    tests: 0,
  };
  const canSave =
    draft !== null &&
    contractIsComplete(manifest) &&
    !hasParameterProblems(rules, inputNames) &&
    !logicIncomplete(rules, inputNames) &&
    (dirty || scenariosChanged) &&
    !busy;

  const canSubmit =
    draft !== null &&
    !dirty &&
    rules.rules.length > 0 &&
    scenarios.length > 0 &&
    !scenariosChanged &&
    !busy &&
    !submitted;

  // Say why submitting is unavailable instead of leaving a silently disabled button.
  const submitBlocker =
    draft === null || canSubmit || submitted || busy
      ? null
      : dirty || scenariosChanged
        ? t('flow.submitSaveFirst')
        : rules.rules.length === 0
          ? t('flow.submitNeedRule')
          : t('flow.submitNeedScenario');

  async function persist(body: {
    manifestJson: string;
    sourceHints: GuidedSourceHintRequest[];
    rulesJson: string | null;
    scenariosJson: string | null;
  }) {
    const { data, error } =
      draft?.id && draft.revision
        ? await editGuidedModuleDraft({ path: { id: draft.id }, body: { ...body, expectedRevision: draft.revision } })
        : await createGuidedModuleDraft({ body });
    if (!data) throw new Error(detailOf(error));
    // Point the address at the saved draft so a reload or a bookmark reopens it; replaceState does not remount the editor.
    if (!draft?.id && data.id) window.history.replaceState(null, '', hashFor('authoring', 'draft:' + data.id));
    setDraft(data);
    setDirty(false);
    setScenariosChanged(false);
    setSubmitted(false);
  }

  async function run(action: () => Promise<void>) {
    setBusy(true);
    setFailed(null);
    try {
      await action();
    } catch (error) {
      setFailed(error instanceof Error && error.message ? error.message : t('flow.saveFailed'));
    } finally {
      setBusy(false);
    }
  }

  const bodyOf = (current: string) => ({
    manifestJson: JSON.stringify({ ...(parseManifest(current) ?? {}), displayName: name.trim() }),
    sourceHints: hints,
    rulesJson: serializeRules(rules),
    scenariosJson: scenarios.length > 0 && rules.rules.length > 0 ? JSON.stringify(scenarios) : null,
  });

  function edit(next: string) {
    setManifest(next);
    setDirty(true);
    setJustSaved(false);
  }

  function editScenarios(change: (previous: Scenario[]) => Scenario[]) {
    setScenarios(change);
    setScenariosChanged(true);
    setJustSaved(false);
  }

  function editRules(next: RulesModel) {
    setRules(next);
    setDirty(true);
    setJustSaved(false);
  }

  function changeHint(inputId: string, next: GuidedSourceHintRequest | null) {
    setHints((previous) => [...previous.filter((item) => item.inputId !== inputId), ...(next ? [next] : [])]);
    setDirty(true);
    setJustSaved(false);
  }

  if (!draft && draftId) {
    return (
      <div className="authoring-flow">
        {failed ? (
          <Notice tone="error">{failed}</Notice>
        ) : (
          <Notice tone="info" announce={false}>
            {t('flow.opening')}
          </Notice>
        )}
      </div>
    );
  }

  if (!draft) {
    return (
      <div className="authoring-flow">
        <Section title={t('flow.newModule')}>
          <form
            className="authoring-flow-fields"
            onSubmit={(event) => {
              event.preventDefault();
              const blank = blankContractManifest(name.trim(), providerKey);
              void run(async () => {
                await persist(bodyOf(blank));
                setManifest(blank);
              });
            }}
          >
            <TextField
              label={t('flow.name')}
              maxLength={128}
              value={name}
              onChange={(event) => setName(event.target.value)}
            />
            <SelectField
              label={t('flow.provider')}
              value={providerKey}
              onChange={(event) => setProviderKey(event.target.value)}
            >
              <option value="">{t('flow.chooseProvider')}</option>
              {providers.map((item) => (
                <option key={item.adapterKey} value={item.adapterKey ?? ''}>
                  {item.displayName ?? item.adapterKey}
                </option>
              ))}
            </SelectField>
            <ActionButton
              type="submit"
              label={t('flow.create')}
              variant="primary"
              busy={busy}
              disabled={!name.trim() || !providerKey}
            />
          </form>
          {failed && <Notice tone="error">{failed}</Notice>}
        </Section>
      </div>
    );
  }

  return (
    <div className="authoring-workbench">
      <header className="authoring-workbench-header">
        <div>
          <h3>{name}</h3>
          <span>{provider?.displayName ?? providerKey}</span>
        </div>
        <div className="authoring-flow-actions">
          <ActionLink href={hashFor('authoring')} label={t('flow.allDrafts')} variant="secondary" />
          {justSaved && !dirty && <Notice tone="success">{t('flow.saved')}</Notice>}
          {failed && <Notice tone="error">{failed}</Notice>}
          {submitted && <Notice tone="success">{t('flow.submitted')}</Notice>}
          {submitBlocker && (
            <Notice tone="info" announce={false}>
              {submitBlocker}
            </Notice>
          )}
          <ActionButton
            label={t('flow.submit')}
            disabled={!canSubmit}
            onClick={() =>
              void run(async () => {
                if (!draft?.id || !draft.revision) return;
                const { data, error } = await submitGuidedModuleRules({
                  path: { id: draft.id },
                  body: { expectedRevision: draft.revision, cases: scenarios },
                });
                if (!data) throw new Error(detailOf(error));
                setSubmitted(true);
              })
            }
          />
          <ActionButton
            label={t('flow.save')}
            variant="primary"
            busy={busy}
            disabled={!canSave}
            onClick={() =>
              void run(async () => {
                await persist(bodyOf(manifest));
                setJustSaved(true);
              })
            }
          />
        </div>
      </header>
      <div className="authoring-workbench-body">
        <nav className="authoring-outline" aria-label={t('flow.outline')}>
          {outline.map((item) => (
            <button
              key={item.id}
              type="button"
              className="authoring-outline-item"
              aria-pressed={selected === item.id}
              onClick={() => setSelected(item.id)}
            >
              <span>{t(item.label)}</span>
              {item.id !== 'tests' && <span className="authoring-count">{counts[item.id]}</span>}
            </button>
          ))}
        </nav>
        <div className="authoring-workbench-editor">
          {selected === 'parameters' && <ParametersEditor rules={rules} takenNames={inputNames} onChange={editRules} />}
          {selected === 'calculations' && (
            <CalculationsEditor rules={rules} inputNames={inputNames} onChange={editRules} />
          )}
          {selected === 'tests' && (
            <TestsEditor
              draftId={draft?.id ?? null}
              revision={draft?.revision ?? null}
              saved={!dirty}
              inputs={inputs.map((input) => ({ name: inputName(input), unit: text(input.canonicalUnit) }))}
              rules={rules}
              scenarios={scenarios}
              onAddScenario={(scenario) => editScenarios((previous) => [...previous, scenario])}
              onRemoveScenario={(index) => editScenarios((previous) => previous.filter((_, at) => at !== index))}
              onChangeExpected={(index, expectedOutcome) =>
                editScenarios((previous) =>
                  previous.map((item, at) => (at === index ? { ...item, expectedOutcome } : item)),
                )
              }
            />
          )}
          {selected === 'tests' && (
            <Section title={t('flow.liveTitle')}>
              <p className="connection-help">{t('flow.liveReadOnly')}</p>
              {draft && !dirty && !scenariosChanged && rules.rules.length > 0 ? (
                <GuidedLivePreview
                  draftId={draft.id ?? ''}
                  revision={draft.revision ?? 0}
                  inputs={inputs.map((input) => ({ id: text(input.id), quantity: text(input.quantity) }))}
                />
              ) : (
                <Notice tone="info" announce={false}>
                  {t('flow.liveNeedsSaved')}
                </Notice>
              )}
            </Section>
          )}
          {selected === 'rules' && (
            <RulesEditor rules={rules} inputNames={inputNames} commands={commands} onChange={editRules} />
          )}
          {selected === 'inputs' && (
            <InputsEditor
              providerKey={providerKey}
              externalSources={provider?.externalSources ?? []}
              manifest={manifest}
              hints={hints}
              onEdit={edit}
              onHint={changeHint}
            />
          )}
        </div>
      </div>
    </div>
  );
}

function InputsEditor({
  providerKey,
  externalSources,
  manifest,
  hints,
  onEdit,
  onHint,
}: {
  providerKey: string;
  externalSources: NonNullable<GuidedProviderResponse['externalSources']>;
  manifest: string;
  hints: GuidedSourceHintRequest[];
  onEdit: (next: string) => void;
  onHint: (inputId: string, next: GuidedSourceHintRequest | null) => void;
}) {
  const [options, setOptions] = useState<SourceOption[] | null>(null);
  const [sourceKind, setSourceKind] = useState('');
  const [dailyField, setDailyField] = useState('');
  const [dailyUnit, setDailyUnit] = useState('');
  const [dailyChoice, setDailyChoice] = useState('');
  const inputs = useMemo(() => inputsOf(manifest), [manifest]);
  const offeredFor = (capabilityId: string) =>
    (options ?? []).filter(
      (option) =>
        option.capabilityId === capabilityId &&
        !inputs.some(
          (input) =>
            input.quantity === option.quantity &&
            (input.requiredCapability === dailyCapability) === (capabilityId === dailyCapability),
        ),
    );
  const offered = offeredFor('engineering.current');
  const offeredDaily = offeredFor(dailyCapability);

  useEffect(() => {
    const controller = new AbortController();
    void loadAdapterOperations(controller.signal)
      .then((adapters) => {
        if (controller.signal.aborted) return;
        setOptions(
          sourceOptionsFrom(
            adapters.filter((adapter) => adapter.adapterKey?.toLowerCase() === providerKey.toLowerCase()),
          ),
        );
      })
      .catch(() => {
        if (!controller.signal.aborted) setOptions([]);
      });
    return () => controller.abort();
  }, [providerKey]);

  return (
    <Section title={t('flow.inputsTitle')}>
      {inputs.length === 0 && (
        <Notice tone="info" announce={false}>
          {t('flow.inputsGuide')}
        </Notice>
      )}
      {inputs.length > 0 && (
        <ul className="authoring-flow-inputs">
          {inputs.map((input) => {
            const id = text(input.id);
            return (
              <li key={id} className="authoring-flow-input">
                <div className="authoring-card-head">
                  <span className="authoring-tag">{t('flow.tagWellData')}</span>
                  <strong>{inputName(input)}</strong>
                  <span className="authoring-card-unit">
                    {t(input.requiredCapability === dailyCapability ? 'flow.sourceDaily' : 'flow.sourceCurrent')}
                  </span>
                  {text(input.alias) && <span className="authoring-card-unit">{text(input.quantity)}</span>}
                  {text(input.canonicalUnit) && (
                    <span className="authoring-card-unit">{text(input.canonicalUnit)}</span>
                  )}
                  <ActionButton
                    label={t('flow.removeInput')}
                    icon={Trash2}
                    iconOnly
                    variant="quiet"
                    onClick={() => {
                      onEdit(removeInput(manifest, id));
                      onHint(id, null);
                    }}
                  />
                </div>
                <div className="authoring-card-body">
                  <NumberField
                    label={t('flow.maximumAge')}
                    min={1}
                    step={1}
                    value={typeof input.maximumAgeSeconds === 'number' ? input.maximumAgeSeconds : ''}
                    onChange={(event) =>
                      onEdit(
                        setInputMaximumAge(manifest, id, event.target.value === '' ? null : Number(event.target.value)),
                      )
                    }
                  />
                  <GuidedSourceHintFields
                    inputId={id}
                    externalSources={externalSources}
                    hint={hints.find((item) => item.inputId === id)}
                    disabled={false}
                    onChange={(next) => onHint(id, next)}
                  />
                </div>
              </li>
            );
          })}
        </ul>
      )}
      {options !== null && options.length === 0 ? (
        <Notice tone="info" announce={false}>
          {t('flow.noInputsOffered')}
        </Notice>
      ) : (
        <div className="authoring-add">
          <SelectField
            label={t('flow.inputSource')}
            value={sourceKind}
            disabled={options === null}
            onChange={(event) => setSourceKind(event.target.value)}
          >
            <option value="">{t('flow.chooseSource')}</option>
            {inputSources.map((kind) => (
              <option key={kind} value={kind}>
                {t(sourceLabels[kind])}
              </option>
            ))}
          </SelectField>
          {sourceKind === 'current' && (
            <SelectField
              label={t('flow.addInput')}
              value=""
              disabled={offered.length === 0}
              onChange={(event) => {
                const option = offered.find((item) => item.quantity === event.target.value);
                if (option) {
                  onEdit(addCurrentInput(manifest, option));
                  setSourceKind('');
                }
              }}
            >
              <option value="">{t('flow.chooseInput')}</option>
              {offered.map((option) => (
                <option key={option.quantity} value={option.quantity}>
                  {option.unit ? option.quantity + ' (' + option.unit + ')' : option.quantity}
                </option>
              ))}
            </SelectField>
          )}
          {sourceKind === 'daily' && (
            <SelectField
              label={t('flow.addDailyInput')}
              value={dailyChoice}
              onChange={(event) => {
                const choice = event.target.value;
                const option = offeredDaily.find((item) => item.quantity === choice);
                if (option) {
                  onEdit(addDailyInput(manifest, option.quantity, option.unit));
                  setDailyChoice('');
                  setSourceKind('');
                } else setDailyChoice(choice);
              }}
            >
              <option value="">{t('flow.chooseInput')}</option>
              {offeredDaily.map((option) => (
                <option key={option.quantity} value={option.quantity}>
                  {option.unit ? option.quantity + ' (' + option.unit + ')' : option.quantity}
                </option>
              ))}
              <option value={otherField}>{t('flow.otherField')}</option>
            </SelectField>
          )}
          {sourceKind === 'daily' && dailyChoice === otherField && (
            <form
              className="authoring-flow-search"
              onSubmit={(event) => {
                event.preventDefault();
                const field = dailyField.trim();
                if (!field) return;
                onEdit(addDailyInput(manifest, field, dailyUnit.trim() || null));
                setDailyField('');
                setDailyUnit('');
                setDailyChoice('');
                setSourceKind('');
              }}
            >
              <TextField
                label={t('flow.dailyField')}
                value={dailyField}
                maxLength={128}
                onChange={(event) => setDailyField(event.target.value)}
              />
              <TextField
                label={t('flow.dailyUnit')}
                value={dailyUnit}
                maxLength={64}
                onChange={(event) => setDailyUnit(event.target.value)}
              />
              <ActionButton type="submit" label={t('flow.add')} disabled={!dailyField.trim()} />
            </form>
          )}
        </div>
      )}
    </Section>
  );
}
