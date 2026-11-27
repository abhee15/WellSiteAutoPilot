import type { AdapterOperationsResponse } from '../api/generated/v1/types.gen';

/**
 * Edits for the portable typed contract of a module (roles are preserved, inputs are edited) as the same saved
 * manifest the server validates. Nothing here invents calculations, sample sources or provider fields.
 */
type Json = Record<string, unknown>;

const identifier = /^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$/;
// Scalar current readings are served under this registered capability; see AdapterOperationDescriptor.
const scalarCurrentCapability = 'engineering.current';
/** A provider's own production-day field, read as the latest finished day; see AdapterReadRules. */
export const dailyCapability = 'engineering.daily';
// A finished production day closes within a day and the record can post late, so two days is the default bound.
const dailyMaximumAgeSeconds = 172800;

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
        (operation.capabilityId !== scalarCurrentCapability && operation.capabilityId !== dailyCapability) ||
        operation.structuredSchemaId != null ||
        !operation.quantity
      )
        continue;
      const capabilityId = operation.capabilityId;
      // The same name can be both a current reading and a daily value, so the capability is part of the key.
      const key = `${capabilityId}|${operation.quantity}`;
      const unit = operation.unit ?? null;
      const existing = byQuantity.get(key);
      if (!existing) {
        byQuantity.set(key, {
          quantity: operation.quantity,
          capabilityId,
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

function inputPorts(manifest: Json): Json[] {
  const list = manifest.dataRequirements;
  return Array.isArray(list) ? list.filter(isObject) : [];
}

export function inputsOf(manifestJson: string): Record<string, unknown>[] {
  const manifest = parseManifest(manifestJson);
  return manifest ? inputPorts(manifest) : [];
}

function scalarSchema(id: string) {
  return { schemaId: 'authoring.' + id + '.number/v1', schema: { type: 'number' } };
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

/** True when every port has a quantity the server will accept; the server remains the authority. */
export function contractIsComplete(source: string | null | undefined): boolean {
  const manifest = parseManifest(source);
  if (!manifest) return false;
  const outputs = Array.isArray(manifest.outputRequirements) ? manifest.outputRequirements.filter(isObject) : [];
  return (
    inputPorts(manifest).every(
      (port) =>
        typeof port.quantity === 'string' &&
        identifier.test(port.quantity) &&
        typeof port.requiredCapability === 'string' &&
        identifier.test(port.requiredCapability),
    ) && outputs.every((port) => typeof port.quantity === 'string' && identifier.test(port.quantity))
  );
}

function withInputs(manifest: Json, ports: Json[]): Json {
  return { ...manifest, dataRequirements: ports };
}

function nextId(ports: Json[]): string {
  const used = new Set(ports.map((port) => port.id));
  let n = ports.length + 1;
  while (used.has('input-' + n)) n += 1;
  return 'input-' + n;
}

function newInput(manifest: Json, ports: Json[]): Json {
  const id = nextId(ports);
  const roles = Array.isArray(manifest.assetRequirements) ? manifest.assetRequirements.filter(isObject) : [];
  return {
    id,
    assetRole: typeof roles[0]?.role === 'string' ? roles[0].role : 'asset',
    quantity: '',
    access: 'Current',
    canonicalUnit: null,
    maximumAgeSeconds: null,
    allowUncertainQuality: false,
    valueSchema: scalarSchema(id),
    // Chosen by picking a registered quantity; a free-text capability would never match an adapter.
    requiredCapability: '',
  };
}

/** The name a module's expressions use for an input: its alias when it has one, otherwise the quantity. */
export function inputName(port: Record<string, unknown>): string {
  return typeof port.alias === 'string' && port.alias !== '' ? port.alias : String(port.quantity ?? '');
}

/** An alias only when the quantity alone would clash with another input, for example a current and a daily reading of one field. */
function aliasIfTaken(ports: Json[], quantity: string, prefix: string): Json {
  return ports.some((port) => inputName(port) === quantity) ? { alias: prefix + quantity } : {};
}

/** Adds a registered current reading as an input, with its registered unit and capability already filled in. */
export function addCurrentInput(manifestJson: string, option: SourceOption): string {
  const manifest = parseManifest(manifestJson);
  if (!manifest || !identifier.test(option.quantity)) return manifestJson;
  const ports = inputPorts(manifest);
  if (ports.some((port) => port.quantity === option.quantity && port.requiredCapability === option.capabilityId))
    return manifestJson;
  const port = {
    ...newInput(manifest, ports),
    quantity: option.quantity,
    requiredCapability: option.capabilityId,
    ...aliasIfTaken(ports, option.quantity, 'Current'),
  };
  return JSON.stringify(withInputs(manifest, [...ports, option.unit ? { ...port, canonicalUnit: option.unit } : port]));
}

/** Removes the input with this id. */
export function removeInput(manifestJson: string, inputId: string): string {
  const manifest = parseManifest(manifestJson);
  if (!manifest) return manifestJson;
  return JSON.stringify(
    withInputs(
      manifest,
      inputPorts(manifest).filter((port) => port.id !== inputId),
    ),
  );
}

/** Changes the maximum age, in seconds, of one input; null means no limit. */
export function setInputMaximumAge(manifestJson: string, inputId: string, seconds: number | null): string {
  const manifest = parseManifest(manifestJson);
  if (!manifest) return manifestJson;
  return JSON.stringify(
    withInputs(
      manifest,
      inputPorts(manifest).map((port) => (port.id === inputId ? { ...port, maximumAgeSeconds: seconds } : port)),
    ),
  );
}

/** Adds a discovered provider daily field as an input. A field already declared as a daily input is left alone. */
export function addDailyInput(manifestJson: string, quantity: string, unit: string | null): string {
  const manifest = parseManifest(manifestJson);
  if (!manifest || !identifier.test(quantity)) return manifestJson;
  const ports = inputPorts(manifest);
  if (ports.some((port) => port.quantity === quantity && port.requiredCapability === dailyCapability))
    return manifestJson;
  const port: Json = {
    ...newInput(manifest, ports),
    quantity,
    canonicalUnit: unit,
    maximumAgeSeconds: dailyMaximumAgeSeconds,
    // The daily record carries no verified quality, so a module reading it must accept uncertain data.
    allowUncertainQuality: true,
    requiredCapability: dailyCapability,
    ...aliasIfTaken(ports, quantity, 'Daily'),
  };
  return JSON.stringify(withInputs(manifest, [...ports, port]));
}
