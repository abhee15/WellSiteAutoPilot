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

/**
 * One path through module authoring: provider, then inputs. Each step appears once the one before it is complete,
 * and offers only what the chosen provider serves. Later steps are added as the backend can run them.
 */
export function AuthoringFlow() {
  const [providers, setProviders] = useState<GuidedProviderResponse[]>([]);
  const [options, setOptions] = useState<SourceOption[] | null>(null);
  const [name, setName] = useState('');
  const [providerKey, setProviderKey] = useState('');
  const [manifest, setManifest] = useState('');
  const [hints, setHints] = useState<GuidedSourceHintRequest[]>([]);
  const [draft, setDraft] = useState<GuidedModuleDraftResponse | null>(null);
  const [dirty, setDirty] = useState(false);
  const [sourceKind, setSourceKind] = useState('');
  const [dailyField, setDailyField] = useState('');
  const [dailyUnit, setDailyUnit] = useState('');
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

  useEffect(() => {
    if (!providerKey) return;
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

  const inputs = useMemo(() => (manifest ? inputsOf(manifest) : []), [manifest]);
  const offered = (options ?? []).filter((option) => !inputs.some((input) => input.quantity === option.quantity));
  const locked = draft !== null;
  const ready = name.trim() !== '' && providerKey !== '';
  const canSave = ready && inputs.length > 0 && contractIsComplete(manifest) && dirty && !busy;

  function changeProvider(key: string) {
    setProviderKey(key);
    setManifest(key ? blankContractManifest('', key) : '');
    setHints([]);
    setOptions(null);
  }

  function edit(next: string) {
    setManifest(next);
    setDirty(true);
    setJustSaved(false);
  }

  function changeHint(inputId: string, next: GuidedSourceHintRequest | null) {
    setHints((previous) => [...previous.filter((item) => item.inputId !== inputId), ...(next ? [next] : [])]);
    setDirty(true);
  }

  async function save() {
    setBusy(true);
    setFailed(false);
    try {
      await persist();
      setJustSaved(true);
    } catch {
      setFailed(true);
    } finally {
      setBusy(false);
    }
  }

  async function persist(): Promise<GuidedModuleDraftResponse> {
    const body = {
      manifestJson: JSON.stringify({ ...(parseManifest(manifest) ?? {}), displayName: name.trim() }),
      sourceHints: hints,
    };
    const { data } =
      draft?.id && draft.revision
        ? await editGuidedModuleDraft({ path: { id: draft.id }, body: { ...body, expectedRevision: draft.revision } })
        : await createGuidedModuleDraft({ body });
    if (!data) throw new Error();
    setDraft(data);
    setDirty(false);
    return data;
  }

  return (
    <div className="authoring-flow">
      <Section title={t('flow.moduleTitle')}>
        <div className="authoring-flow-fields">
          <TextField
            label={t('flow.name')}
            maxLength={128}
            value={name}
            onChange={(event) => {
              setName(event.target.value);
              setDirty(true);
            }}
          />
          <SelectField
            label={t('flow.provider')}
            value={providerKey}
            disabled={locked}
            onChange={(event) => changeProvider(event.target.value)}
          >
            <option value="">{t('flow.chooseProvider')}</option>
            {providers.map((item) => (
              <option key={item.adapterKey} value={item.adapterKey ?? ''}>
                {item.displayName ?? item.adapterKey}
              </option>
            ))}
          </SelectField>
        </div>
      </Section>

      {ready && (
        <Section title={t('flow.inputsTitle')}>
          {inputs.length > 0 && (
            <ul className="authoring-flow-inputs">
              {inputs.map((input) => {
                const id = text(input.id);
                return (
                  <li key={id} className="authoring-flow-input">
                    <div className="authoring-flow-input-name">
                      <strong>{text(input.quantity)}</strong>
                      {text(input.canonicalUnit) && <span>{text(input.canonicalUnit)}</span>}
                    </div>
                    <NumberField
                      label={t('flow.maximumAge')}
                      min={1}
                      step={1}
                      value={typeof input.maximumAgeSeconds === 'number' ? input.maximumAgeSeconds : ''}
                      onChange={(event) =>
                        edit(
                          setInputMaximumAge(
                            manifest,
                            id,
                            event.target.value === '' ? null : Number(event.target.value),
                          ),
                        )
                      }
                    />
                    <GuidedSourceHintFields
                      inputId={id}
                      externalSources={provider?.externalSources ?? []}
                      hint={hints.find((item) => item.inputId === id)}
                      disabled={false}
                      onChange={(next) => changeHint(id, next)}
                    />
                    <ActionButton
                      label={t('flow.removeInput')}
                      icon={Trash2}
                      iconOnly
                      variant="quiet"
                      onClick={() => {
                        edit(removeInput(manifest, id));
                        changeHint(id, null);
                      }}
                    />
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
            <>
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
                      edit(addCurrentInput(manifest, option));
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
                    edit(addDailyInput(manifest, field, dailyUnit.trim() || null));
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
            </>
          )}
        </Section>
      )}

      {ready && (
        <div className="authoring-flow-actions">
          <ActionButton
            label={t('flow.save')}
            variant="primary"
            busy={busy}
            disabled={!canSave}
            onClick={() => void save()}
          />
          {justSaved && !dirty && <Notice tone="success">{t('flow.saved')}</Notice>}
          {failed && <Notice tone="error">{t('flow.saveFailed')}</Notice>}
        </div>
      )}
    </div>
  );
}
