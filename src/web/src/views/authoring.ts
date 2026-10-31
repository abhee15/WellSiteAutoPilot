import type { AuthoringBuildState, AuthoringTestState, AuthoringValidationState } from '../api/generated/v1/types.gen';

/** The authoring project document as the server serializes it (candidate contract, scalar graph plus opaque code steps). */
export type OperandKind = 'input' | 'parameter' | 'step' | 'literal';
export interface Operand { kind: OperandKind; ref?: string | null; port?: string | null; value?: string | null }
export interface ValueSchema { schemaId: string; schema: unknown }
export interface DraftInput {
  id: string; label: string; quantity: string; unit?: string | null; assetRole: string;
  maximumAgeSeconds?: number | null; allowUncertainQuality?: boolean;
  valueSchema?: ValueSchema | null; requiredCapability?: string | null;
}
export interface DraftParameter { id: string; label: string; defaultValue: string; unit?: string | null }
export interface DraftCode { language: string; entryPoint: string; source: string; inputPorts: string[]; outputPorts: string[] }
export interface DraftStep {
  id: string; label: string; kind: 'calculation' | 'collection' | 'code'; operation?: string | null;
  operands?: Operand[] | null; code?: DraftCode | null;
}
export interface DraftCondition { id: string; label: string; left: Operand; comparator: string; right: Operand }
export interface DraftOutput {
  id: string; label: string; quantity: string; unit: string; source: Operand; valueSchema?: ValueSchema | null;
}
export interface DraftAction {
  id: string; label: string; condition: string; title: string; detail: string;
  quantity?: string | null; unit?: string | null; suggestedValue?: Operand | null;
}
export interface DraftModule {
  moduleId: string; version: string; displayName: string; publisher: string;
  description?: string | null; requiredAdapterKey?: string | null;
}
export interface DraftDocument {
  schema: string; module: DraftModule; profile: string; assetRoles: { role: string; requiredAssetTypeKey?: string | null }[];
  inputs: DraftInput[]; parameters: DraftParameter[]; steps: DraftStep[]; conditions: DraftCondition[];
  outputs: DraftOutput[]; actions: DraftAction[];
}

export type SelectionKind = 'module' | 'input' | 'parameter' | 'step' | 'condition' | 'output' | 'action';
export interface Selection { kind: SelectionKind; id: string }

/** The server sends the document as untyped JSON; accept only an object carrying the collections the editor walks. */
export function readDocument(value: unknown): DraftDocument | null {
  if (!value || typeof value !== 'object') return null;
  const doc = value as Partial<DraftDocument>;
  if (!doc.module || !Array.isArray(doc.inputs) || !Array.isArray(doc.steps) || !Array.isArray(doc.outputs)) return null;
  return {
    ...(doc as DraftDocument),
    assetRoles: doc.assetRoles ?? [], parameters: doc.parameters ?? [],
    conditions: doc.conditions ?? [], actions: doc.actions ?? [],
  };
}

export function operandKey(operand: Operand): string {
  return operand.kind === 'literal' ? 'literal' : `${operand.kind}:${operand.ref ?? ''}`;
}

export function operandText(operand: Operand | null | undefined): string {
  if (!operand) return '—';
  if (operand.kind === 'literal') return operand.value ?? '';
  return operand.port ? `${operand.ref}.${operand.port}` : operand.ref ?? '';
}

export interface OutlineNode {
  kind: SelectionKind; id: string; label: string;
  /** Elements this one reads, as `kind:id`. */
  uses: string[];
}

const nodeKey = (kind: SelectionKind, id: string) => `${kind}:${id}`;
const refs = (...operands: (Operand | null | undefined)[]): string[] =>
  operands.filter((o): o is Operand => !!o && o.kind !== 'literal' && !!o.ref).map(o => nodeKey(o.kind as SelectionKind, o.ref ?? ''));

/** One row per element with the elements it depends on; this drives the outline and the used-by view. */
export function buildOutline(doc: DraftDocument): OutlineNode[] {
  const nodes: OutlineNode[] = [];
  doc.inputs.forEach(item => nodes.push({ kind: 'input', id: item.id, label: item.label, uses: [] }));
  doc.parameters.forEach(item => nodes.push({ kind: 'parameter', id: item.id, label: item.label, uses: [] }));
  doc.steps.forEach(item => nodes.push({
    kind: 'step', id: item.id, label: item.label,
    uses: item.kind === 'code' ? (item.code?.inputPorts ?? []).map(port => nodeKey('input', port)) : refs(...(item.operands ?? [])),
  }));
  doc.conditions.forEach(item => nodes.push({ kind: 'condition', id: item.id, label: item.label, uses: refs(item.left, item.right) }));
  doc.outputs.forEach(item => nodes.push({ kind: 'output', id: item.id, label: item.label, uses: refs(item.source) }));
  doc.actions.forEach(item => nodes.push({
    kind: 'action', id: item.id, label: item.label,
    uses: [nodeKey('condition', item.condition), ...refs(item.suggestedValue)],
  }));
  return nodes;
}

export function usedBy(outline: readonly OutlineNode[], selection: Selection): OutlineNode[] {
  const key = nodeKey(selection.kind, selection.id);
  return outline.filter(node => node.uses.includes(key));
}

export function usesOf(outline: readonly OutlineNode[], node: OutlineNode): OutlineNode[] {
  return node.uses.map(key => outline.find(candidate => nodeKey(candidate.kind, candidate.id) === key))
    .filter((candidate): candidate is OutlineNode => !!candidate);
}

/** Server issue paths name elements by stable id (for example `steps/shortfall/operands/0`). */
const PATH_KINDS: Record<string, SelectionKind> = {
  inputs: 'input', parameters: 'parameter', steps: 'step', conditions: 'condition', outputs: 'output', actions: 'action',
};
export function selectionForIssuePath(path: string | null | undefined): Selection | null {
  const [collection, id] = (path ?? '').split('/');
  const kind = PATH_KINDS[collection ?? ''];
  if (kind && id) return { kind, id };
  return collection === 'module' ? { kind: 'module', id: 'module' } : null;
}

export function uniqueId(existing: readonly string[], base: string): string {
  for (let index = 1; ; index += 1) {
    const candidate = `${base}-${index}`;
    if (!existing.includes(candidate)) return candidate;
  }
}

export const DEFAULT_OPERATION_ARITY: Record<string, [number, number]> = {
  add: [2, 8], subtract: [2, 2], multiply: [2, 8], divide: [2, 2], min: [2, 8], max: [2, 8], abs: [1, 1],
};

export function blankOperand(doc: DraftDocument): Operand {
  const input = doc.inputs.find(item => !item.valueSchema);
  return input ? { kind: 'input', ref: input.id } : { kind: 'literal', value: '0' };
}

export function addStep(doc: DraftDocument): { doc: DraftDocument; id: string } {
  const id = uniqueId(doc.steps.map(step => step.id), 'step');
  const operand = blankOperand(doc);
  const step: DraftStep = {
    id, label: 'New calculation', kind: 'calculation', operation: 'add',
    operands: [operand, { kind: 'literal', value: '1' }],
  };
  return { doc: { ...doc, steps: [...doc.steps, step] }, id };
}

export const COLLECTION_OPERATIONS = ['count', 'sum', 'min', 'max', 'mean'] as const;

/** Adds a collection step over the first structured input; null when the draft has none. */
export function addCollectionStep(doc: DraftDocument): { doc: DraftDocument; id: string } | null {
  const input = doc.inputs.find(item => item.valueSchema);
  if (!input) return null;
  const id = uniqueId(doc.steps.map(step => step.id), 'collection');
  const step: DraftStep = {
    id, label: 'New collection step', kind: 'collection', operation: 'count',
    operands: [{ kind: 'input', ref: input.id }],
  };
  return { doc: { ...doc, steps: [...doc.steps, step] }, id };
}

export function addInput(doc: DraftDocument): { doc: DraftDocument; id: string } {
  const id = uniqueId(doc.inputs.map(item => item.id), 'input');
  const input: DraftInput = {
    id, label: 'New input', quantity: 'Quantity', unit: '', assetRole: doc.assetRoles[0]?.role ?? 'well', maximumAgeSeconds: 60,
  };
  return { doc: { ...doc, inputs: [...doc.inputs, input] }, id };
}

export function addParameter(doc: DraftDocument): { doc: DraftDocument; id: string } {
  const id = uniqueId(doc.parameters.map(item => item.id), 'parameter');
  return { doc: { ...doc, parameters: [...doc.parameters, { id, label: 'New parameter', defaultValue: '0', unit: '' }] }, id };
}

export function addOutput(doc: DraftDocument): { doc: DraftDocument; id: string } {
  const id = uniqueId(doc.outputs.map(item => item.id), 'output');
  const output: DraftOutput = { id, label: 'New output', quantity: 'Quantity', unit: '', source: blankOperand(doc) };
  return { doc: { ...doc, outputs: [...doc.outputs, output] }, id };
}

/** Removing an element is only offered when nothing else reads it, so an edit never leaves a dangling edge. */
export function removeElement(doc: DraftDocument, selection: Selection): DraftDocument {
  const keep = <T extends { id: string }>(items: T[]) => items.filter(item => item.id !== selection.id);
  switch (selection.kind) {
    case 'input': return { ...doc, inputs: keep(doc.inputs) };
    case 'parameter': return { ...doc, parameters: keep(doc.parameters) };
    case 'step': return { ...doc, steps: keep(doc.steps) };
    case 'condition': return { ...doc, conditions: keep(doc.conditions) };
    case 'output': return { ...doc, outputs: keep(doc.outputs) };
    case 'action': return { ...doc, actions: keep(doc.actions) };
    default: return doc;
  }
}

export function replaceById<T extends { id: string }>(items: T[], id: string, change: Partial<T>): T[] {
  return items.map(item => (item.id === id ? { ...item, ...change } : item));
}

export function setOperandAt(operands: Operand[], index: number, operand: Operand): Operand[] {
  return operands.map((current, position) => (position === index ? operand : current));
}

/** The state vocabulary is the server's; tones are how the shared Status component draws it. */
export function validationTone(state: AuthoringValidationState | undefined) {
  return state === 'Valid' ? 'success' : state === 'Invalid' ? 'error' : state === 'Stale' ? 'warning' : 'neutral';
}
export function buildTone(state: AuthoringBuildState | undefined) {
  return state === 'Succeeded' ? 'success' : state === 'Failed' ? 'error' : state === 'Stale' ? 'warning'
    : state === 'Building' ? 'info' : 'neutral';
}
export function testTone(state: AuthoringTestState | undefined) {
  return state === 'Passed' ? 'success' : state === 'Failed' ? 'error' : state === 'Stale' ? 'warning' : 'neutral';
}

export function documentsEqual(a: DraftDocument | null, b: DraftDocument | null): boolean {
  return JSON.stringify(a) === JSON.stringify(b);
}
