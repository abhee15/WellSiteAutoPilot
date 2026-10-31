import type { ReactNode } from 'react';
import { ActionButton } from '../components/ActionButton';
import { t } from '../localization';
import type { MessageKey } from '../localization';
import {
  COLLECTION_OPERATIONS, DEFAULT_OPERATION_ARITY, blankOperand, operandKey, replaceById, setOperandAt,
} from './authoring';
import type { DraftDocument, Operand, Selection } from './authoring';

const OPERATION_LABELS: Record<string, MessageKey> = {
  add: 'authoring.operation.add', subtract: 'authoring.operation.subtract', multiply: 'authoring.operation.multiply',
  divide: 'authoring.operation.divide', min: 'authoring.operation.min', max: 'authoring.operation.max',
  abs: 'authoring.operation.abs',
};
const COLLECTION_LABELS: Record<string, MessageKey> = {
  count: 'authoring.collection.count', sum: 'authoring.collection.sum', min: 'authoring.collection.min',
  max: 'authoring.collection.max', mean: 'authoring.collection.mean',
};
const COMPARATOR_LABELS: Record<string, MessageKey> = {
  lt: 'authoring.comparator.lt', le: 'authoring.comparator.le', gt: 'authoring.comparator.gt',
  ge: 'authoring.comparator.ge', eq: 'authoring.comparator.eq', ne: 'authoring.comparator.ne',
};

function Field({ label, children }: { label: string; children: ReactNode }) {
  return <label className="authoring-field"><span>{label}</span>{children}</label>;
}

function TextField({ label, value, onChange, readOnly = false, multiline = false, mono = false }: {
  label: string; value: string | null | undefined; onChange?: (value: string) => void;
  readOnly?: boolean; multiline?: boolean; mono?: boolean;
}) {
  const common = {
    value: value ?? '', readOnly, className: mono ? 'authoring-mono' : undefined,
    onChange: (event: { target: { value: string } }) => onChange?.(event.target.value),
  };
  return <Field label={label}>{multiline ? <textarea rows={12} spellCheck={false} {...common} /> : <input type="text" {...common} />}</Field>;
}

/** Edits one edge endpoint: another element's value or a literal decimal. */
function OperandEditor({ label, operand, doc, selfStepId, onChange }: {
  label: string; operand: Operand; doc: DraftDocument; selfStepId?: string; onChange: (operand: Operand) => void;
}) {
  const scalarInputs = doc.inputs.filter(item => !item.valueSchema);
  const steps = doc.steps.filter(step => step.kind !== 'code' && step.id !== selfStepId);
  const current = operandKey(operand);
  const known = current === 'literal' || scalarInputs.some(i => current === `input:${i.id}`)
    || doc.parameters.some(p => current === `parameter:${p.id}`) || steps.some(s => current === `step:${s.id}`);
  return <div className="authoring-operand">
    <Field label={label}>
      <select value={current} onChange={event => {
        const [kind, ...rest] = event.target.value.split(':');
        onChange(kind === 'literal' ? { kind: 'literal', value: operand.value ?? '0' }
          : { kind: kind as Operand['kind'], ref: rest.join(':') });
      }}>
        {!known && <option value={current}>{t('authoring.operand.none')}</option>}
        <option value="literal">{t('authoring.operand.literal')}</option>
        {scalarInputs.map(item => <option key={item.id} value={`input:${item.id}`}>{t('authoring.operand.input', { name: item.label })}</option>)}
        {doc.parameters.map(item => <option key={item.id} value={`parameter:${item.id}`}>{t('authoring.operand.parameter', { name: item.label })}</option>)}
        {steps.map(item => <option key={item.id} value={`step:${item.id}`}>{t('authoring.operand.step', { name: item.label })}</option>)}
      </select>
    </Field>
    {operand.kind === 'literal' && <TextField label={t('authoring.field.literal')} mono value={operand.value}
      onChange={value => onChange({ kind: 'literal', value })} />}
  </div>;
}

export function ElementEditor({ doc, selection, onChange, onRemove, removable }: {
  doc: DraftDocument; selection: Selection; onChange: (doc: DraftDocument) => void;
  onRemove: () => void; removable: boolean;
}) {
  const remove = selection.kind === 'module' ? null : <div className="authoring-remove">
    <ActionButton label={t('authoring.remove', { kind: t(KIND_LABELS[selection.kind]).toLowerCase() })}
      disabled={!removable} onClick={onRemove} />
    {!removable && <small>{t('authoring.removeBlocked')}</small>}
  </div>;

  if (selection.kind === 'module') {
    const set = (change: Partial<DraftDocument['module']>) => onChange({ ...doc, module: { ...doc.module, ...change } });
    return <div className="authoring-form">
      <TextField label={t('authoring.field.moduleId')} value={doc.module.moduleId} onChange={moduleId => set({ moduleId })} />
      <TextField label={t('authoring.field.version')} value={doc.module.version} onChange={version => set({ version })} />
      <TextField label={t('authoring.field.displayName')} value={doc.module.displayName} onChange={displayName => set({ displayName })} />
      <TextField label={t('authoring.field.publisher')} value={doc.module.publisher} onChange={publisher => set({ publisher })} />
      <TextField label={t('authoring.field.description')} value={doc.module.description} onChange={description => set({ description })} />
    </div>;
  }

  if (selection.kind === 'input') {
    const input = doc.inputs.find(item => item.id === selection.id);
    if (!input) return null;
    const set = (change: Partial<typeof input>) => onChange({ ...doc, inputs: replaceById(doc.inputs, input.id, change) });
    return <div className="authoring-form">
      <TextField label={t('authoring.field.id')} value={input.id} readOnly />
      <TextField label={t('authoring.field.label')} value={input.label} onChange={label => set({ label })} />
      <TextField label={t('authoring.field.quantity')} value={input.quantity} onChange={quantity => set({ quantity })} />
      <TextField label={t('authoring.field.unit')} value={input.unit} onChange={unit => set({ unit })} />
      <Field label={t('authoring.field.maximumAge')}>
        <input type="number" min={1} value={input.maximumAgeSeconds ?? ''}
          onChange={event => set({ maximumAgeSeconds: event.target.value === '' ? null : Number(event.target.value) })} />
      </Field>
      {input.valueSchema && <p className="authoring-note">{t('authoring.structuredInput')}</p>}
      {remove}
    </div>;
  }

  if (selection.kind === 'parameter') {
    const parameter = doc.parameters.find(item => item.id === selection.id);
    if (!parameter) return null;
    const set = (change: Partial<typeof parameter>) => onChange({ ...doc, parameters: replaceById(doc.parameters, parameter.id, change) });
    return <div className="authoring-form">
      <TextField label={t('authoring.field.id')} value={parameter.id} readOnly />
      <TextField label={t('authoring.field.label')} value={parameter.label} onChange={label => set({ label })} />
      <TextField label={t('authoring.field.defaultValue')} mono value={parameter.defaultValue} onChange={defaultValue => set({ defaultValue })} />
      <TextField label={t('authoring.field.unit')} value={parameter.unit} onChange={unit => set({ unit })} />
      {remove}
    </div>;
  }

  if (selection.kind === 'step') {
    const step = doc.steps.find(item => item.id === selection.id);
    if (!step) return null;
    const set = (change: Partial<typeof step>) => onChange({ ...doc, steps: replaceById(doc.steps, step.id, change) });
    if (step.kind === 'code') {
      const code = step.code;
      return <div className="authoring-form">
        <TextField label={t('authoring.field.id')} value={step.id} readOnly />
        <TextField label={t('authoring.field.label')} value={step.label} onChange={label => set({ label })} />
        <p className="authoring-note">{t('authoring.codeStep')}</p>
        {code && <>
          <TextField label={t('authoring.field.entryPoint')} value={code.entryPoint} mono
            onChange={entryPoint => set({ code: { ...code, entryPoint } })} />
          <TextField label={t('authoring.field.code')} value={code.source} multiline mono
            onChange={source => set({ code: { ...code, source } })} />
        </>}
        {remove}
      </div>;
    }
    const operands = step.operands ?? [];
    if (step.kind === 'collection') {
      const source = operands[0] ?? { kind: 'input' as const, ref: '' };
      const structured = doc.inputs.filter(item => item.valueSchema);
      const setSource = (change: Partial<typeof source>) => set({ operands: [{ ...source, kind: 'input', ...change }] });
      return <div className="authoring-form">
        <TextField label={t('authoring.field.id')} value={step.id} readOnly />
        <TextField label={t('authoring.field.label')} value={step.label} onChange={label => set({ label })} />
        <Field label={t('authoring.field.operation')}>
          <select value={step.operation ?? 'count'} onChange={event => set({ operation: event.target.value })}>
            {COLLECTION_OPERATIONS.map(value => <option key={value} value={value}>{t(COLLECTION_LABELS[value])}</option>)}
          </select>
        </Field>
        <Field label={t('authoring.field.collectionInput')}>
          <select value={source.ref ?? ''} onChange={event => setSource({ ref: event.target.value })}>
            {structured.map(item => <option key={item.id} value={item.id}>{item.label}</option>)}
          </select>
        </Field>
        <TextField label={t('authoring.field.collectionPort')} mono value={source.port}
          onChange={port => setSource({ port: port === '' ? null : port })} />
        <p className="authoring-note">{t('authoring.collectionStep')}</p>
        {remove}
      </div>;
    }
    const [minimum, maximum] = DEFAULT_OPERATION_ARITY[step.operation ?? 'add'] ?? [2, 8];
    return <div className="authoring-form">
      <TextField label={t('authoring.field.id')} value={step.id} readOnly />
      <TextField label={t('authoring.field.label')} value={step.label} onChange={label => set({ label })} />
      <Field label={t('authoring.field.operation')}>
        <select value={step.operation ?? 'add'} onChange={event => {
          const operation = event.target.value;
          const [low, high] = DEFAULT_OPERATION_ARITY[operation] ?? [2, 8];
          const next = operands.slice(0, high);
          while (next.length < low) next.push(blankOperand(doc));
          set({ operation, operands: next });
        }}>
          {Object.entries(OPERATION_LABELS).map(([value, key]) => <option key={value} value={value}>{t(key)}</option>)}
        </select>
      </Field>
      {operands.map((operand, index) => <OperandEditor key={index} label={t('authoring.field.operand', { number: index + 1 })}
        operand={operand} doc={doc} selfStepId={step.id}
        onChange={next => set({ operands: setOperandAt(operands, index, next) })} />)}
      <div className="authoring-inline">
        <ActionButton label={t('authoring.addOperand')} disabled={operands.length >= maximum}
          onClick={() => set({ operands: [...operands, blankOperand(doc)] })} />
        <ActionButton label={t('authoring.removeOperand')} disabled={operands.length <= minimum}
          onClick={() => set({ operands: operands.slice(0, -1) })} />
      </div>
      {remove}
    </div>;
  }

  if (selection.kind === 'condition') {
    const condition = doc.conditions.find(item => item.id === selection.id);
    if (!condition) return null;
    const set = (change: Partial<typeof condition>) => onChange({ ...doc, conditions: replaceById(doc.conditions, condition.id, change) });
    return <div className="authoring-form">
      <TextField label={t('authoring.field.id')} value={condition.id} readOnly />
      <TextField label={t('authoring.field.label')} value={condition.label} onChange={label => set({ label })} />
      <OperandEditor label={t('authoring.field.left')} operand={condition.left} doc={doc} onChange={left => set({ left })} />
      <Field label={t('authoring.field.comparator')}>
        <select value={condition.comparator} onChange={event => set({ comparator: event.target.value })}>
          {Object.entries(COMPARATOR_LABELS).map(([value, key]) => <option key={value} value={value}>{t(key)}</option>)}
        </select>
      </Field>
      <OperandEditor label={t('authoring.field.right')} operand={condition.right} doc={doc} onChange={right => set({ right })} />
      {remove}
    </div>;
  }

  if (selection.kind === 'output') {
    const output = doc.outputs.find(item => item.id === selection.id);
    if (!output) return null;
    const set = (change: Partial<typeof output>) => onChange({ ...doc, outputs: replaceById(doc.outputs, output.id, change) });
    return <div className="authoring-form">
      <TextField label={t('authoring.field.id')} value={output.id} readOnly />
      <TextField label={t('authoring.field.label')} value={output.label} onChange={label => set({ label })} />
      <TextField label={t('authoring.field.quantity')} value={output.quantity} onChange={quantity => set({ quantity })} />
      <TextField label={t('authoring.field.unit')} value={output.unit} onChange={unit => set({ unit })} />
      {output.source.kind === 'step' && output.source.port
        ? <TextField label={t('authoring.field.port')} value={`${output.source.ref}.${output.source.port}`} readOnly />
        : <OperandEditor label={t('authoring.field.source')} operand={output.source} doc={doc} onChange={source => set({ source })} />}
      {remove}
    </div>;
  }

  const action = doc.actions.find(item => item.id === selection.id);
  if (!action) return null;
  const set = (change: Partial<typeof action>) => onChange({ ...doc, actions: replaceById(doc.actions, action.id, change) });
  return <div className="authoring-form">
    <TextField label={t('authoring.field.id')} value={action.id} readOnly />
    <TextField label={t('authoring.field.label')} value={action.label} onChange={label => set({ label })} />
    <Field label={t('authoring.field.condition')}>
      <select value={action.condition} onChange={event => set({ condition: event.target.value })}>
        {doc.conditions.map(item => <option key={item.id} value={item.id}>{item.label}</option>)}
      </select>
    </Field>
    <TextField label={t('authoring.field.title')} value={action.title} onChange={title => set({ title })} />
    <TextField label={t('authoring.field.detail')} value={action.detail} onChange={detail => set({ detail })} />
    {action.suggestedValue && <OperandEditor label={t('authoring.field.suggested')} operand={action.suggestedValue} doc={doc}
      onChange={suggestedValue => set({ suggestedValue })} />}
    {remove}
  </div>;
}

export const KIND_LABELS: Record<Selection['kind'], MessageKey> = {
  module: 'authoring.kind.module', input: 'authoring.kind.input', parameter: 'authoring.kind.parameter',
  step: 'authoring.kind.step', condition: 'authoring.kind.condition', output: 'authoring.kind.output',
  action: 'authoring.kind.action',
};
