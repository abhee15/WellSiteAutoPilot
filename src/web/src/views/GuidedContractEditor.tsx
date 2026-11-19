import { useEffect, useId, useState } from 'react';
import type {
  AdapterOperationsResponse,
  GuidedExternalSourceResponse,
  GuidedSourceHintRequest,
} from '../api/generated/v1/types.gen';
import { ActionButton } from '../components/ActionButton';
import { CheckboxField, NumberField, SelectField, TextField } from '../components/FormFields';
import { Notice } from '../components/Notice';
import { t } from '../localization';
import { loadAdapterOperations } from './adapterOperations';
import { GuidedSourceHintFields } from './GuidedSourceHintFields';

/**
 * Edits the portable typed contract (roles are preserved, inputs and outputs are edited) as the same saved V2
 * manifest the server validates. It never invents calculations, sample sources or provider fields.
 */
type Json = Record<string, unknown>;
export type PortKind = 'input' | 'output';

const listKey = { input: 'dataRequirements', output: 'outputRequirements' } as const;
const maximumPorts = 64;
const identifier = /^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$/;
// Scalar current readings are served under this registered capability; see AdapterOperationDescriptor.
const scalarCurrentCapability = 'engineering.current';
/** A provider's own production-day field, read as the latest finished day; see AdapterReadRules. */
export const dailyCapability = 'engineering.daily';
// A finished production day closes within a day and the record can post late, so two days is the default bound.
const dailyMaximumAgeSeconds = 172800;
type ScalarType = 'number' | 'string';

/** A scalar current reading that at least one registered adapter operation declares it can serve. */
export type SourceOption = { quantity: string; capabilityId: string; unit: string | null; adapters: string[] };

/** Derived only from the Adapter Operations registry; nothing here is hardcoded per provider. */
export function sourceOptionsFrom(adapters: readonly AdapterOperationsResponse[]): SourceOption[] {
  const byQuantity = new Map<string, SourceOption & { unitConflict: boolean }>();
  for (const adapter of adapters) {
    const adapterName = adapter.displayName ?? adapter.adapterKey ?? '';
    for (const operation of adapter.operations ?? []) {
      if (
        operation.capability !== 'CurrentData' ||
        operation.capabilityId !== scalarCurrentCapability ||
        operation.structuredSchemaId != null ||
        !operation.quantity
      )
        continue;
      const unit = operation.unit ?? null;
      const existing = byQuantity.get(operation.quantity);
      if (!existing) {
        byQuantity.set(operation.quantity, {
          quantity: operation.quantity,
          capabilityId: scalarCurrentCapability,
          unit,
          adapters: [adapterName],
          unitConflict: false,
        });
        continue;
      }
      if (!existing.adapters.includes(adapterName)) existing.adapters.push(adapterName);
      // Providers that disagree on a unit never get a silently chosen one.
      if (existing.unit !== unit) existing.unitConflict = true;
    }
  }
  return [...byQuantity.values()]
    .map(({ unitConflict, ...option }) => (unitConflict ? { ...option, unit: null } : option))
    .sort((a, b) => a.quantity.localeCompare(b.quantity));
}

/** A structured current read a provider registers: the capability, schema id and the exact JSON Schema it serves. */
export type StructuredOption = { quantity: string; schemaId: string; schema: unknown; adapters: string[] };

const structuredCurrentCapability = 'structured.current';

/** Structured sources come only from the registry, which supplies the provider's own schema verbatim. */
export function structuredOptionsFrom(adapters: readonly AdapterOperationsResponse[]): StructuredOption[] {
  const byQuantity = new Map<string, StructuredOption>();
  for (const adapter of adapters) {
    const adapterName = adapter.displayName ?? adapter.adapterKey ?? '';
    for (const operation of adapter.operations ?? []) {
      if (
        operation.capability !== 'CurrentData' ||
        operation.capabilityId !== structuredCurrentCapability ||
        !operation.quantity ||
        !operation.structuredSchemaId ||
        !operation.structuredJsonSchema
      )
        continue;
      let schema: unknown;
      try {
        schema = JSON.parse(operation.structuredJsonSchema);
      } catch {
        continue;
      }
      const existing = byQuantity.get(operation.quantity);
      if (!existing) {
        byQuantity.set(operation.quantity, {
          quantity: operation.quantity,
          schemaId: operation.structuredSchemaId,
          schema,
          adapters: [adapterName],
        });
      } else if (existing.schemaId === operation.structuredSchemaId && !existing.adapters.includes(adapterName)) {
        existing.adapters.push(adapterName);
      }
    }
  }
  return [...byQuantity.values()].sort((a, b) => a.quantity.localeCompare(b.quantity));
}

type Catalog =
  | { status: 'loading' }
  | { status: 'unavailable' }
  | { status: 'ready'; options: SourceOption[]; structured: StructuredOption[]; adapters: AdapterOperationsResponse[] };

function isObject(value: unknown): value is Json {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value);
}

export function parseManifest(source: string | null | undefined): Json | null {
  if (!source) return null;
  try {
    const value: unknown = JSON.parse(source);
    return isObject(value) ? value : null;
  } catch {
    return null;
  }
}

export function inputsOf(manifestJson: string): Record<string, unknown>[] {
  const manifest = parseManifest(manifestJson);
  return manifest ? portsOf(manifest, 'input') : [];
}

function portsOf(manifest: Json, kind: PortKind): Json[] {
  const list = manifest[listKey[kind]];
  return Array.isArray(list) ? list.filter(isObject) : [];
}

function scalarSchema(id: string, type: ScalarType) {
  return {
    schemaId: 'authoring.' + id + '.' + type + '/v1',
    schema: type === 'number' ? { type } : { type, minLength: 1, maxLength: 128 },
  };
}

function scalarTypeOf(port: Json): ScalarType | null {
  const schema = isObject(port.valueSchema) && isObject(port.valueSchema.schema) ? port.valueSchema.schema : null;
  return schema && (schema.type === 'number' || schema.type === 'string') && !('items' in schema) ? schema.type : null;
}

/** A blank, valid-to-save contract: one neutral asset role and no ports yet. */
export function blankContractManifest(name: string, adapterKey?: string): string {
  return JSON.stringify({
    ...(adapterKey ? { requiredAdapterKey: adapterKey } : {}),
    manifestVersion: 2,
    moduleId: 'authoring.' + crypto.randomUUID(),
    version: '1.0.0',
    displayName: name,
    publisher: 'WellSite AutoPilot',
    runtime: 'DotNet',
    executionProfile: 'ScheduledOneShot',
    entryPoint: 'contract.unset',
    minimumSdkVersion: '1.2.0',
    assetRequirements: [{ role: 'asset', minimumCount: 1, maximumCount: 1, requiredTraits: [] }],
    dataRequirements: [],
    commandRequirements: [],
    outputRequirements: [],
  });
}

/**
 * The contract of a saved draft for another provider: a new module identity, the same roles, inputs, outputs and
 * parameters. Source hints are provider-specific and sample cases are a separate feature, so neither is carried over.
 * Inputs the new provider does not serve stay and are flagged by the editor, not silently dropped.
 */
export function copyContractManifest(source: string, adapterKey: string, copySuffix: string): string | null {
  const manifest = parseManifest(source);
  if (!manifest) return null;
  const name = typeof manifest.displayName === 'string' ? manifest.displayName : '';
  return JSON.stringify({
    ...manifest,
    moduleId: 'authoring.' + crypto.randomUUID(),
    displayName: (name + ' ' + copySuffix).trim(),
    requiredAdapterKey: adapterKey,
  });
}

export function isContractOnlyManifest(source: string | null | undefined): boolean {
  return parseManifest(source)?.entryPoint === 'contract.unset';
}

/** True when every port has a quantity the server will accept; the server remains the authority. */
export function contractIsComplete(source: string | null | undefined): boolean {
  const manifest = parseManifest(source);
  if (!manifest) return false;
  return (['input', 'output'] as const).every((kind) =>
    portsOf(manifest, kind).every(
      (port) =>
        typeof port.quantity === 'string' &&
        identifier.test(port.quantity) &&
        (kind === 'output' ||
          (typeof port.requiredCapability === 'string' && identifier.test(port.requiredCapability))),
    ),
  );
}

/** The first port that blocks saving, for telling the author why; null when the contract is complete. */
export function firstIncompletePort(source: string | null | undefined): string | null {
  const manifest = parseManifest(source);
  if (!manifest) return null;
  for (const kind of ['input', 'output'] as const)
    for (const port of portsOf(manifest, kind))
      if (!(
        typeof port.quantity === 'string' &&
        identifier.test(port.quantity) &&
        (kind === 'output' || (typeof port.requiredCapability === 'string' && identifier.test(port.requiredCapability)))
      ))
        return (typeof port.quantity === 'string' && port.quantity) || (typeof port.id === 'string' && port.id) || kind;
  return null;
}

function withPorts(manifest: Json, kind: PortKind, ports: Json[]): Json {
  return { ...manifest, [listKey[kind]]: ports };
}

function nextId(ports: Json[], kind: PortKind): string {
  const used = new Set(ports.map((port) => port.id));
  let n = ports.length + 1;
  while (used.has(kind + '-' + n)) n += 1;
  return kind + '-' + n;
}

function newStructuredPort(manifest: Json, ports: Json[], option: StructuredOption): Json {
  const id = nextId(ports, 'input');
  const roles = Array.isArray(manifest.assetRequirements) ? manifest.assetRequirements.filter(isObject) : [];
  return {
    id,
    assetRole: typeof roles[0]?.role === 'string' ? roles[0].role : 'asset',
    quantity: option.quantity,
    access: 'Current',
    canonicalUnit: null,
    maximumAgeSeconds: null,
    allowUncertainQuality: false,
    // The provider's own schema, so the contract can never drift from what the adapter returns.
    valueSchema: { schemaId: option.schemaId, schema: option.schema },
    requiredCapability: structuredCurrentCapability,
  };
}

function newPort(manifest: Json, kind: PortKind, ports: Json[]): Json {
  const id = nextId(ports, kind);
  // Starts empty: a made-up default name could never be saved, so every new port would begin in an error state.
  const quantity = '';
  const valueSchema = scalarSchema(id, 'number');
  if (kind === 'output') return { id, quantity, unit: null, valueSchema };
  const roles = Array.isArray(manifest.assetRequirements) ? manifest.assetRequirements.filter(isObject) : [];
  return {
    id,
    assetRole: typeof roles[0]?.role === 'string' ? roles[0].role : 'asset',
    quantity,
    access: 'Current',
    canonicalUnit: null,
    maximumAgeSeconds: null,
    allowUncertainQuality: false,
    valueSchema,
    // Chosen by picking a registered quantity; a free-text capability would never match an adapter.
    requiredCapability: '',
  };
}

/** Adds a registered current reading as an input, with its registered unit and capability already filled in. */
export function addCurrentInput(manifestJson: string, option: SourceOption): string {
  const manifest = parseManifest(manifestJson);
  if (!manifest || !identifier.test(option.quantity)) return manifestJson;
  const ports = portsOf(manifest, 'input');
  if (ports.some((port) => port.quantity === option.quantity && port.requiredCapability === option.capabilityId))
    return manifestJson;
  const port = {
    ...newPort(manifest, 'input', ports),
    quantity: option.quantity,
    requiredCapability: option.capabilityId,
  };
  return JSON.stringify(
    withPorts(manifest, 'input', [...ports, option.unit ? { ...port, canonicalUnit: option.unit } : port]),
  );
}

/** Removes the input with this id. */
export function removeInput(manifestJson: string, inputId: string): string {
  const manifest = parseManifest(manifestJson);
  if (!manifest) return manifestJson;
  return JSON.stringify(
    withPorts(
      manifest,
      'input',
      portsOf(manifest, 'input').filter((port) => port.id !== inputId),
    ),
  );
}

/** Changes the maximum age, in seconds, of one input; null means no limit. */
export function setInputMaximumAge(manifestJson: string, inputId: string, seconds: number | null): string {
  const manifest = parseManifest(manifestJson);
  if (!manifest) return manifestJson;
  return JSON.stringify(
    withPorts(
      manifest,
      'input',
      portsOf(manifest, 'input').map((port) => (port.id === inputId ? { ...port, maximumAgeSeconds: seconds } : port)),
    ),
  );
}

/** Adds a discovered provider daily field as an input. A field already declared as a daily input is left alone. */
export function addDailyInput(manifestJson: string, quantity: string, unit: string | null): string {
  const manifest = parseManifest(manifestJson);
  if (!manifest || !identifier.test(quantity)) return manifestJson;
  const ports = portsOf(manifest, 'input');
  if (ports.some((port) => port.quantity === quantity && port.requiredCapability === dailyCapability))
    return manifestJson;
  const id = nextId(ports, 'input');
  const roles = Array.isArray(manifest.assetRequirements) ? manifest.assetRequirements.filter(isObject) : [];
  const port: Json = {
    id,
    assetRole: typeof roles[0]?.role === 'string' ? roles[0].role : 'asset',
    quantity,
    access: 'Current',
    canonicalUnit: unit,
    maximumAgeSeconds: dailyMaximumAgeSeconds,
    // The daily record carries no verified quality, so a module reading it must accept uncertain data.
    allowUncertainQuality: true,
    valueSchema: scalarSchema(id, 'number'),
    requiredCapability: dailyCapability,
  };
  return JSON.stringify(withPorts(manifest, 'input', [...ports, port]));
}

export function GuidedContractEditor({
  manifestJson,
  disabled,
  onChange,
  hints = [],
  externalSources = [],
  onHintsChange,
}: {
  manifestJson: string;
  disabled: boolean;
  onChange: (manifestJson: string) => void;
  hints?: GuidedSourceHintRequest[];
  externalSources?: GuidedExternalSourceResponse[];
  onHintsChange?: (hints: GuidedSourceHintRequest[]) => void;
}) {
  const [selection, setSelection] = useState<{ kind: PortKind; index: number }>({ kind: 'input', index: 0 });
  const [catalog, setCatalog] = useState<Catalog>({ status: 'loading' });
  const quantityListId = useId();
  useEffect(() => {
    const controller = new AbortController();
    void loadAdapterOperations(controller.signal)
      .then((adapters) => {
        if (!controller.signal.aborted)
          setCatalog({
            status: 'ready',
            options: sourceOptionsFrom(adapters),
            structured: structuredOptionsFrom(adapters),
            adapters,
          });
      })
      .catch(() => {
        if (!controller.signal.aborted) setCatalog({ status: 'unavailable' });
      });
    return () => controller.abort();
  }, []);
  const manifest = parseManifest(manifestJson);
  if (!manifest) return null;

  const lists = { input: portsOf(manifest, 'input'), output: portsOf(manifest, 'output') };
  const kind =
    lists[selection.kind].length > 0 || lists[selection.kind === 'input' ? 'output' : 'input'].length === 0
      ? selection.kind
      : selection.kind === 'input'
        ? 'output'
        : 'input';
  const index = Math.min(selection.index, Math.max(0, lists[kind].length - 1));
  const port = lists[kind][index];

  function commit(next: Json) {
    onChange(JSON.stringify(next));
  }

  function update(patch: Json) {
    const ports = lists[kind].map((item, position) => (position === index ? { ...item, ...patch } : item));
    commit(withPorts(manifest!, kind, ports));
  }

  function add(addKind: PortKind) {
    const ports = [...lists[addKind], newPort(manifest!, addKind, lists[addKind])];
    commit(withPorts(manifest!, addKind, ports));
    setSelection({ kind: addKind, index: ports.length - 1 });
  }

  function addStructured(option: StructuredOption) {
    const ports = [...lists.input, newStructuredPort(manifest!, lists.input, option)];
    commit(withPorts(manifest!, 'input', ports));
    setSelection({ kind: 'input', index: ports.length - 1 });
  }

  function remove() {
    commit(
      withPorts(
        manifest!,
        kind,
        lists[kind].filter((_, position) => position !== index),
      ),
    );
    setSelection({ kind, index: Math.max(0, index - 1) });
  }

  function changeHint(inputId: string, next: GuidedSourceHintRequest | null) {
    onHintsChange?.([...hints.filter((item) => item.inputId !== inputId), ...(next ? [next] : [])]);
  }

  function textValue(value: unknown) {
    return typeof value === 'string' ? value : '';
  }

  const unitKey = kind === 'input' ? 'canonicalUnit' : 'unit';
  const scalar = port ? scalarTypeOf(port) : null;
  // A provider-first module only offers what its own provider serves.
  const providerKey =
    typeof manifest.requiredAdapterKey === 'string' ? manifest.requiredAdapterKey.toLowerCase() : null;
  const providerAdapters =
    catalog.status === 'ready'
      ? catalog.adapters.filter((adapter) => !providerKey || adapter.adapterKey?.toLowerCase() === providerKey)
      : [];
  const options = catalog.status === 'ready' ? sourceOptionsFrom(providerAdapters) : [];
  const structuredOptions = catalog.status === 'ready' ? structuredOptionsFrom(providerAdapters) : [];
  const matched = port ? options.find((option) => option.quantity === textValue(port.quantity)) : undefined;
  // Only scalar inputs take their capability from the registry; structured inputs keep what was saved.
  const registryInput = kind === 'input' && scalar !== null;

  function changeQuantity(value: string) {
    const option = options.find((candidate) => candidate.quantity === value);
    // A daily field is the provider's own name, so it keeps its capability unless a registered quantity is chosen.
    const daily = port?.requiredCapability === dailyCapability && !option;
    update({
      quantity: value,
      ...(registryInput && !daily ? { requiredCapability: option?.capabilityId ?? '' } : {}),
      // The registered unit is authoritative; a stale unit from a previous quantity is never kept.
      ...(registryInput && option?.unit ? { [unitKey]: option.unit } : {}),
    });
  }

  function quantityMessage(): { error?: string; helper?: string } {
    if (!port) return {};
    if (textValue(port.quantity) === '') return { helper: t('guided.portQuantityPrompt') };
    if (!identifier.test(textValue(port.quantity))) return { error: t('guided.portIdentifierInvalid') };
    if (!registryInput) return {};
    if (port.requiredCapability === dailyCapability) return { helper: t('guided.portDailyField') };
    if (matched) return { helper: t('guided.portAvailableFrom', { adapters: matched.adapters.join(', ') }) };
    return catalog.status === 'ready' ? { error: t('guided.portNoSource') } : {};
  }

  return (
    <section aria-label={t('guided.contractEditor')}>
      <div className="guided-authoring-contract-groups">
        <div className="guided-authoring-contract-list">
          {(['input', 'output'] as const).map((group) => (
            <section key={group} aria-label={t(group === 'input' ? 'guided.contractInputs' : 'guided.contractOutputs')}>
              <h4>{t(group === 'input' ? 'guided.contractInputs' : 'guided.contractOutputs')}</h4>
              {lists[group].length === 0 ? (
                <p>{t(group === 'input' ? 'guided.editorNoInputs' : 'guided.editorNoOutputs')}</p>
              ) : (
                <ul className="guided-authoring-contract-ports">
                  {lists[group].map((item, position) => (
                    <li key={textValue(item.id) || position}>
                      <ActionButton
                        label={textValue(item.quantity) || textValue(item.id)}
                        variant="quiet"
                        aria-pressed={kind === group && index === position}
                        onClick={() => setSelection({ kind: group, index: position })}
                      />
                    </li>
                  ))}
                </ul>
              )}
              <ActionButton
                label={t(group === 'input' ? 'guided.addInput' : 'guided.addOutput')}
                disabled={disabled || lists[group].length >= maximumPorts}
                onClick={() => add(group)}
              />
              {group === 'input' &&
                structuredOptions.map((option) => (
                  <ActionButton
                    key={option.quantity}
                    label={t('guided.addStructuredInput', { quantity: option.quantity })}
                    disabled={disabled || lists.input.length >= maximumPorts}
                    onClick={() => addStructured(option)}
                  />
                ))}
            </section>
          ))}
        </div>
        {port && (
          <section className="guided-authoring-contract-details" aria-label={t('guided.contractDetails')}>
            <h4>{textValue(port.quantity) || textValue(port.id)}</h4>
            <div className="guided-authoring-fields">
              <TextField
                label={t('guided.portQuantity')}
                maxLength={128}
                value={textValue(port.quantity)}
                disabled={disabled || (kind === 'input' && scalar === null)}
                list={registryInput ? quantityListId : undefined}
                {...quantityMessage()}
                onChange={(event) => changeQuantity(event.target.value)}
              />
              {registryInput && (
                <datalist id={quantityListId}>
                  {options.map((option) => (
                    <option key={option.quantity} value={option.quantity} label={option.adapters.join(', ')} />
                  ))}
                </datalist>
              )}
              {registryInput && catalog.status === 'unavailable' && (
                <Notice tone="warning" announce={false}>
                  {t('guided.catalogUnavailable')}
                </Notice>
              )}
              {registryInput && catalog.status === 'ready' && options.length === 0 && (
                <Notice tone="info" announce={false}>
                  {t('guided.catalogEmpty')}
                </Notice>
              )}
              <TextField
                label={t('guided.portUnit')}
                maxLength={64}
                value={textValue(port[unitKey])}
                disabled={disabled}
                onChange={(event) => update({ [unitKey]: event.target.value === '' ? null : event.target.value })}
              />
              {scalar ? (
                <SelectField
                  label={t('guided.portType')}
                  value={scalar}
                  disabled={disabled}
                  onChange={(event) =>
                    update({ valueSchema: scalarSchema(textValue(port.id), event.target.value as ScalarType) })
                  }
                >
                  <option value="number">{t('guided.typeNumber')}</option>
                  <option value="string">{t('guided.typeText')}</option>
                </SelectField>
              ) : (
                <Notice tone="info" announce={false}>
                  {t('guided.portStructured')}
                </Notice>
              )}
              {kind === 'input' && (
                <>
                  <SelectField
                    label={t('guided.portAccess')}
                    value={textValue(port.access) || 'Current'}
                    disabled={disabled}
                    onChange={(event) => update({ access: event.target.value })}
                  >
                    <option value="Current">{t('guided.accessCurrent')}</option>
                    <option value="History">{t('guided.accessHistory')}</option>
                  </SelectField>
                  <NumberField
                    label={t('guided.portMaxAge')}
                    min={1}
                    step={1}
                    value={typeof port.maximumAgeSeconds === 'number' ? port.maximumAgeSeconds : ''}
                    disabled={disabled}
                    onChange={(event) =>
                      update({ maximumAgeSeconds: event.target.value === '' ? null : Number(event.target.value) })
                    }
                  />
                  <CheckboxField
                    label={t('guided.portUncertain')}
                    checked={port.allowUncertainQuality === true}
                    disabled={disabled}
                    onChange={(event) => update({ allowUncertainQuality: event.target.checked })}
                  />
                  <TextField
                    label={t('guided.portCapability')}
                    value={textValue(port.requiredCapability)}
                    readOnly
                    helper={t('guided.portCapabilityHelper')}
                  />
                  {onHintsChange && catalog.status === 'ready' && (
                    <GuidedSourceHintFields
                      inputId={textValue(port.id)}
                      externalSources={externalSources}
                      hint={hints.find((item) => item.inputId === textValue(port.id))}
                      disabled={disabled}
                      onChange={(next) => changeHint(textValue(port.id), next)}
                    />
                  )}
                </>
              )}
            </div>
            <ActionButton label={t('guided.removePort')} disabled={disabled} onClick={remove} />
          </section>
        )}
      </div>
    </section>
  );
}
