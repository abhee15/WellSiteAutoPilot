import { useEffect, useMemo, useState } from 'react';
import { Trash2 } from 'lucide-react';
import { createGuidedModuleDraft, editGuidedModuleDraft, listGuidedModuleProviders } from '../api/generated/v1/sdk.gen';
import type {
  GuidedModuleDraftResponse,
  GuidedProviderResponse,
  GuidedSourceHintRequest,
} from '../api/generated/v1/types.gen';
import { ActionButton } from '../components/ActionButton';
import { NumberField, SelectField, TextField } from '../components/FormFields';
import { Notice } from '../components/Notice';
import { Section } from '../components/Section';
import { t } from '../localization';
import { loadAdapterOperations } from './adapterOperations';
import { ParametersEditor, hasParameterProblems } from './AuthoringParameters';
import { emptyRules, serializeRules } from './rulesModel';
import type { RulesModel } from './rulesModel';
import {
  addCurrentInput,
  addDailyInput,
  blankContractManifest,
  contractIsComplete,
  inputsOf,
  parseManifest,
  removeInput,
  setInputMaximumAge,
  sourceOptionsFrom,
} from './GuidedContractEditor';
import type { SourceOption } from './GuidedContractEditor';
import { GuidedSourceHintFields } from './GuidedSourceHintFields';
import './authoringFlow.css';

// Where a reading can come from. Each kind is added here once its readings can be listed and run.
const inputSources = ['current', 'daily'] as const;
const sourceLabels = { current: 'flow.sourceCurrent', daily: 'flow.sourceDaily' } as const;

const text = (value: unknown) => (typeof value === 'string' ? value : '');

type Section = 'inputs' | 'parameters';
const outline: { id: Section; label: 'flow.outlineInputs' | 'flow.outlineParameters' }[] = [
  { id: 'inputs', label: 'flow.outlineInputs' },
  { id: 'parameters', label: 'flow.outlineParameters' },
];

/**
 * Module authoring workbench. A module is created with a name and a provider, then edited here: an outline of the
 * parts of the module, and the editor for the selected part. Parts are added to the outline as they can be run.
 */
export function AuthoringFlow() {
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
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    void listGuidedModuleProviders({ signal: controller.signal })
      .then(({ data }) => {
        if (!controller.signal.aborted && data) setProviders(data);
      })
      .catch(() => undefined);
    return () => controller.abort();
  }, []);

  const provider = providers.find((item) => item.adapterKey === providerKey);
  const inputs = useMemo(() => (manifest ? inputsOf(manifest) : []), [manifest]);
  const inputNames = useMemo(() => inputs.map((input) => String(input.quantity ?? '')), [inputs]);
  const canSave =
    draft !== null && contractIsComplete(manifest) && !hasParameterProblems(rules, inputNames) && dirty && !busy;

  async function persist(body: {
    manifestJson: string;
    sourceHints: GuidedSourceHintRequest[];
    rulesJson: string | null;
  }) {
    const { data } =
      draft?.id && draft.revision
        ? await editGuidedModuleDraft({ path: { id: draft.id }, body: { ...body, expectedRevision: draft.revision } })
        : await createGuidedModuleDraft({ body });
    if (!data) throw new Error();
    setDraft(data);
    setDirty(false);
  }

  async function run(action: () => Promise<void>) {
    setBusy(true);
    setFailed(false);
    try {
      await action();
    } catch {
      setFailed(true);
    } finally {
      setBusy(false);
    }
  }

  const bodyOf = (current: string) => ({
    manifestJson: JSON.stringify({ ...(parseManifest(current) ?? {}), displayName: name.trim() }),
    sourceHints: hints,
    rulesJson: serializeRules(rules),
  });

  function edit(next: string) {
    setManifest(next);
    setDirty(true);
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
          {failed && <Notice tone="error">{t('flow.saveFailed')}</Notice>}
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
          {justSaved && !dirty && <Notice tone="success">{t('flow.saved')}</Notice>}
          {failed && <Notice tone="error">{t('flow.saveFailed')}</Notice>}
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
              <span className="authoring-count">{item.id === 'inputs' ? inputs.length : rules.parameters.length}</span>
            </button>
          ))}
        </nav>
        <div className="authoring-workbench-editor">
          {selected === 'parameters' && <ParametersEditor rules={rules} takenNames={inputNames} onChange={editRules} />}
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
  const inputs = useMemo(() => inputsOf(manifest), [manifest]);
  const offered = (options ?? []).filter((option) => !inputs.some((input) => input.quantity === option.quantity));

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
      {inputs.length > 0 && (
        <ul className="authoring-flow-inputs">
          {inputs.map((input) => {
            const id = text(input.id);
            return (
              <li key={id} className="authoring-flow-input">
                <div className="authoring-card-head">
                  <span className="authoring-tag">{t('flow.tagWellData')}</span>
                  <strong>{text(input.quantity)}</strong>
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
            <form
              className="authoring-flow-search"
              onSubmit={(event) => {
                event.preventDefault();
                const field = dailyField.trim();
                if (!field) return;
                onEdit(addDailyInput(manifest, field, dailyUnit.trim() || null));
                setDailyField('');
                setDailyUnit('');
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
