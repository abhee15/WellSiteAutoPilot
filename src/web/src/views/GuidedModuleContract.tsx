import { useState } from 'react';
import { ActionButton } from '../components/ActionButton';
import { t } from '../localization';

type ValueShape = {
  type?: string;
  items?: ValueShape;
  properties?: Record<string, ValueShape>;
  minItems?: number;
  maxItems?: number;
};

type ValueSchema = {
  schemaId?: string;
  schema?: ValueShape;
};

type ContractPort = {
  id: string;
  quantity: string;
  assetRole?: string;
  access?: string;
  canonicalUnit?: string | null;
  unit?: string | null;
  requiredCapability?: string | null;
  maximumAgeSeconds?: number | null;
  allowUncertainQuality?: boolean;
  valueSchema?: ValueSchema | null;
};

type Contract = {
  inputs: ContractPort[];
  outputs: ContractPort[];
  runtime?: string;
  moduleId?: string;
  version?: string;
};

const visiblePortLimit = 32;
const visiblePropertyLimit = 12;

function isPort(value: unknown): value is ContractPort {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const port = value as Partial<ContractPort>;
  return typeof port.id === 'string' && typeof port.quantity === 'string';
}

function parseContract(source: string): Contract | null {
  try {
    const value: unknown = JSON.parse(source);
    if (!value || typeof value !== 'object' || Array.isArray(value)) return null;
    const manifest = value as Record<string, unknown>;
    return {
      inputs: Array.isArray(manifest.dataRequirements) ? manifest.dataRequirements.filter(isPort) : [],
      outputs: Array.isArray(manifest.outputRequirements) ? manifest.outputRequirements.filter(isPort) : [],
      runtime: typeof manifest.runtime === 'string' ? manifest.runtime : undefined,
      moduleId: typeof manifest.moduleId === 'string' ? manifest.moduleId : undefined,
      version: typeof manifest.version === 'string' ? manifest.version : undefined,
    };
  } catch {
    return null;
  }
}

function shapeLabel(shape: ValueShape | undefined): string {
  if (!shape?.type) return t('guided.contractUnspecified');
  if (shape.type === 'array') return t('guided.contractArrayOf', { item: shapeLabel(shape.items) });
  if (shape.type === 'object') return t('guided.contractRecord');
  return shape.type;
}

type PortKind = 'input' | 'output';
type PortSelection = { kind: PortKind; index: number };

function ContractPorts({
  ports,
  kind,
  selected,
  onSelect,
}: {
  ports: ContractPort[];
  kind: PortKind;
  selected: PortSelection;
  onSelect: (selection: PortSelection) => void;
}) {
  return (
    <section aria-label={t(kind === 'input' ? 'guided.contractInputs' : 'guided.contractOutputs')}>
      <h4>{t(kind === 'input' ? 'guided.contractInputs' : 'guided.contractOutputs')}</h4>
      {ports.length === 0 ? (
        <p>{t(kind === 'input' ? 'guided.contractNoInputs' : 'guided.contractNoOutputs')}</p>
      ) : (
        <>
          <ul className="guided-authoring-contract-ports">
            {ports.slice(0, visiblePortLimit).map((port, index) => (
              <li key={port.id}>
                <ActionButton
                  label={port.quantity}
                  variant="quiet"
                  aria-pressed={selected.kind === kind && selected.index === index}
                  onClick={() => onSelect({ kind, index })}
                />
                <span>{shapeLabel(port.valueSchema?.schema)}</span>
                {(port.canonicalUnit || port.unit) && <span>{port.canonicalUnit || port.unit}</span>}
              </li>
            ))}
          </ul>
          {ports.length > visiblePortLimit && <p>{t('guided.contractLimited', { count: visiblePortLimit })}</p>}
        </>
      )}
    </section>
  );
}

function ContractPortDetails({ port }: { port: ContractPort }) {
  const shape = port.valueSchema?.schema;
  const properties = shape?.type === 'array' ? shape.items?.properties : shape?.properties;
  const fields = properties ? Object.entries(properties) : [];
  return (
    <section className="guided-authoring-contract-details" aria-label={t('guided.contractDetails')}>
      <h4>{port.quantity}</h4>
      <dl>
        <dt>{t('guided.contractPortId')}</dt>
        <dd>{port.id}</dd>
        {port.assetRole && (
          <>
            <dt>{t('guided.contractAssetRole')}</dt>
            <dd>{port.assetRole}</dd>
          </>
        )}
        {port.access && (
          <>
            <dt>{t('guided.contractAccess')}</dt>
            <dd>{port.access}</dd>
          </>
        )}
        {port.requiredCapability && (
          <>
            <dt>{t('guided.contractCapability')}</dt>
            <dd>{port.requiredCapability}</dd>
          </>
        )}
        {typeof port.maximumAgeSeconds === 'number' && (
          <>
            <dt>{t('guided.contractMaxAge')}</dt>
            <dd>{t('guided.contractSeconds', { count: port.maximumAgeSeconds })}</dd>
          </>
        )}
        {typeof port.allowUncertainQuality === 'boolean' && (
          <>
            <dt>{t('guided.contractUncertainQuality')}</dt>
            <dd>{t(port.allowUncertainQuality ? 'adapterReference.yes' : 'adapterReference.no')}</dd>
          </>
        )}
        {port.valueSchema?.schemaId && (
          <>
            <dt>{t('guided.contractSchema')}</dt>
            <dd>{port.valueSchema.schemaId}</dd>
          </>
        )}
        {shape?.type === 'array' && typeof shape.maxItems === 'number' && (
          <>
            <dt>{t('guided.contractMaxItems')}</dt>
            <dd>{shape.maxItems}</dd>
          </>
        )}
        {fields.length > 0 && (
          <>
            <dt>{t('guided.contractFields')}</dt>
            <dd>
              {fields
                .slice(0, visiblePropertyLimit)
                .map(([name, field]) => name + ': ' + shapeLabel(field))
                .join(', ')}
              {fields.length > visiblePropertyLimit && '…'}
            </dd>
          </>
        )}
      </dl>
    </section>
  );
}

export function GuidedModuleContract({
  manifestJson,
  initiallyOpen = false,
  showExecutionBoundary = false,
}: {
  manifestJson: string | null | undefined;
  initiallyOpen?: boolean;
  showExecutionBoundary?: boolean;
}) {
  const [selection, setSelection] = useState<PortSelection>({ kind: 'input', index: 0 });
  if (!manifestJson) return null;
  const contract = parseContract(manifestJson);
  if (!contract) return null;

  const selectedPorts = selection.kind === 'input' ? contract.inputs : contract.outputs;
  const selectedPort = selectedPorts[selection.index];
  const firstPort = contract.inputs[0] ?? contract.outputs[0];
  const runtimeLabel = contract.runtime
    ? t('guided.executionRuntime', { runtime: contract.runtime })
    : t('guided.executionUnknown');

  return (
    <details className="guided-authoring-contract" open={initiallyOpen}>
      <summary>
        {t('guided.contractHeading')} ·{' '}
        {t('guided.contractCounts', {
          inputs: contract.inputs.length,
          outputs: contract.outputs.length,
        })}
      </summary>
      <p>{t('guided.contractBoundary')}</p>
      <div className="guided-authoring-contract-groups">
        <div className="guided-authoring-contract-list" data-execution-boundary={showExecutionBoundary}>
          <ContractPorts ports={contract.inputs} kind="input" selected={selection} onSelect={setSelection} />
          {showExecutionBoundary && (
            <section className="guided-authoring-execution-boundary" aria-label={t('guided.executionBoundary')}>
              <h4>{t('guided.executionBoundary')}</h4>
              <p>{runtimeLabel}</p>
              {contract.moduleId && (
                <p>
                  {contract.moduleId}
                  {contract.version ? ' · ' + contract.version : ''}
                </p>
              )}
              <p>{t('guided.executionOpaque')}</p>
            </section>
          )}
          <ContractPorts ports={contract.outputs} kind="output" selected={selection} onSelect={setSelection} />
        </div>
        {showExecutionBoundary && <p className="guided-authoring-execution-note">{t('guided.executionUntraced')}</p>}
        {(selectedPort ?? firstPort) && <ContractPortDetails port={(selectedPort ?? firstPort)!} />}
      </div>
    </details>
  );
}
