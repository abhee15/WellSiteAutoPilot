import type { DraftInput, DraftOutput, DraftParameter, DraftStep, ModuleBindings, ModuleDraft } from './authoring';

export type NodeKind = 'input' | 'parameter' | 'step' | 'output';
export type Selection = { kind: 'module' } | { kind: NodeKind; name: string };

export const kindLabel: Record<NodeKind, string> = { input: 'Input', parameter: 'Parameter', step: 'Calculation', output: 'Output' };

export const emptyDraft: ModuleDraft = { name: 'New module', description: null, inputs: [], parameters: [], steps: [], outputs: [] };

export function allNames(draft: ModuleDraft): string[] {
  return [...draft.inputs, ...draft.parameters, ...draft.steps, ...draft.outputs].map(node => node.name);
}

export function uniqueName(draft: ModuleDraft, base: string): string {
  const taken = new Set(allNames(draft));
  if (!taken.has(base)) return base;
  let n = 2;
  while (taken.has(`${base}${n}`)) n += 1;
  return `${base}${n}`;
}

export function addNode(draft: ModuleDraft, kind: NodeKind): { draft: ModuleDraft; name: string } {
  const name = uniqueName(draft, { input: 'Input', parameter: 'Parameter', step: 'Step', output: 'Result' }[kind]);
  switch (kind) {
    case 'input': return { name, draft: { ...draft, inputs: [...draft.inputs, { name, quantity: '', required: true }] } };
    case 'parameter': return { name, draft: { ...draft, parameters: [...draft.parameters, { name, value: '0' }] } };
    case 'step': return { name, draft: { ...draft, steps: [...draft.steps, { name, expression: '0' }] } };
    case 'output': return { name, draft: { ...draft, outputs: [...draft.outputs, { name, kind: 'value', expression: '0' }] } };
  }
}

export function removeNode(draft: ModuleDraft, kind: NodeKind, name: string): ModuleDraft {
  const keep = <T extends { name: string }>(items: T[]) => items.filter(item => item.name !== name);
  return {
    ...draft,
    inputs: kind === 'input' ? keep(draft.inputs) : draft.inputs,
    parameters: kind === 'parameter' ? keep(draft.parameters) : draft.parameters,
    steps: kind === 'step' ? keep(draft.steps) : draft.steps,
    outputs: kind === 'output' ? keep(draft.outputs) : draft.outputs,
  };
}

/** Replaces a name used as a value in an expression, leaving quoted text and function names alone. */
export function renameInExpression(expression: string, from: string, to: string): string {
  const pattern = new RegExp(`\\b${from}\\b(?!\\s*\\()`, 'g');
  return expression.split(/("[^"]*"|'[^']*')/).map((part, index) => index % 2 === 1 ? part : part.replace(pattern, to)).join('');
}

type Patch = Partial<DraftInput & DraftParameter & DraftStep & DraftOutput>;

export function updateNode(draft: ModuleDraft, kind: NodeKind, name: string, patch: Patch): ModuleDraft {
  const next = patch.name ?? name;
  const renaming = next !== name;
  const apply = <T extends { name: string }>(items: T[], active: boolean): T[] =>
    items.map(item => active && item.name === name ? { ...item, ...patch } : item);
  const identifier = /^[A-Za-z_][A-Za-z0-9_]*$/;
  const rewrite = <T extends { expression: string }>(items: T[]): T[] =>
    renaming && identifier.test(name) && identifier.test(next) ? items.map(item => ({ ...item, expression: renameInExpression(item.expression, name, next) })) : items;
  return {
    ...draft,
    inputs: apply(draft.inputs, kind === 'input'),
    parameters: apply(draft.parameters, kind === 'parameter'),
    steps: rewrite(apply(draft.steps, kind === 'step')),
    outputs: rewrite(apply(draft.outputs, kind === 'output')).map(output => renaming && output.when && identifier.test(name) && identifier.test(next)
      ? { ...output, when: renameInExpression(output.when, name, next) } : output),
  };
}

/** Bindings are keyed by input name, so they follow a renamed or removed input. */
export function moveBindings(bindings: ModuleBindings, from: string, to: string | null): ModuleBindings {
  const inputs = { ...(bindings.inputs ?? {}) };
  if (!(from in inputs)) return bindings;
  const moved = inputs[from];
  delete inputs[from];
  if (to) inputs[to] = moved;
  return { ...bindings, inputs };
}

export function nodeKindOf(draft: ModuleDraft, name: string): NodeKind | null {
  if (draft.inputs.some(node => node.name === name)) return 'input';
  if (draft.parameters.some(node => node.name === name)) return 'parameter';
  if (draft.steps.some(node => node.name === name)) return 'step';
  if (draft.outputs.some(node => node.name === name)) return 'output';
  return null;
}
