import { ConnectionsError, json, send } from './connections';
import type { SetPointPlan } from './connections';

export type InputSource = { input: string; basis?: string | null };
export type DraftInput = { name: string; sources: InputSource[]; required: boolean; label?: string | null };
export type DraftParameter = { name: string; value: string; label?: string | null };
export type DraftStep = { name: string; expression: string; label?: string | null };
export type DraftOutput = { name: string; expression: string; kind: 'value' | 'setpoint'; unit?: string | null; target?: string | null; label?: string | null };
export type ModuleDraft = {
  name: string; description: string | null;
  inputs: DraftInput[]; parameters: DraftParameter[]; steps: DraftStep[]; outputs: DraftOutput[];
};
export type Starter = { id: string; title: string; summary: string; draft: ModuleDraft };

export type Problem = { severity: 'Error' | 'Warning'; node: string; message: string };
export type GraphNode = { name: string; kind: 'input' | 'parameter' | 'step' | 'output'; dependsOn: string[]; usedBy: string[] };
export type Analysis = { valid: boolean; problems: Problem[]; graph: GraphNode[]; order: string[]; fingerprint: string };

export type Val = { kind: 'Number' | 'Flag' | 'Text' | 'Unavailable'; number: number | null; flag: boolean | null; text: string | null; reason: string | null };
export type InputTrace = {
  name: string; resolution: string; value: Val; unit: string | null; source: string | null; basis: string | null; usedInput: string | null;
  reasons: string[]; candidates: { source: string; basis: string; description: string }[]; required: boolean;
};
export type NodeResult = { name: string; value: Val; expression: string | null; dependsOn: string[] };
export type OutputResult = { name: string; kind: string; value: Val; unit: string | null; target: string | null; expression: string; plan: SetPointPlan | null; note: string | null };
export type ModuleOutcome = 'Runs' | 'Skipped' | 'NeedsSetup' | 'Unavailable' | 'Invalid';
export type ModuleRun = {
  outcome: ModuleOutcome; reasons: string[]; fingerprint: string;
  inputs: InputTrace[]; parameters: NodeResult[]; steps: NodeResult[]; outputs: OutputResult[]; problems: Problem[];
};

export function showVal(value: Val, unit?: string | null): string {
  if (value.kind === 'Unavailable') return `unavailable (${value.reason ?? 'no value'})`;
  const text = value.kind === 'Number' ? String(Number((value.number ?? 0).toFixed(5)))
    : value.kind === 'Flag' ? (value.flag ? 'true' : 'false') : value.text ?? '';
  return unit ? `${text} ${unit}` : text;
}

function isDraft(body: unknown): body is ModuleDraft {
  return !!body && typeof body === 'object' && Array.isArray((body as ModuleDraft).inputs) && Array.isArray((body as ModuleDraft).outputs)
    && Array.isArray((body as ModuleDraft).steps) && Array.isArray((body as ModuleDraft).parameters);
}

/** Checks an imported or pasted description before it replaces the draft being edited. */
export function parseDraft(text: string): ModuleDraft {
  let body: unknown;
  try { body = JSON.parse(text); } catch { throw new ConnectionsError('invalid'); }
  if (!isDraft(body) || typeof body.name !== 'string') throw new ConnectionsError('invalid');
  return {
    ...body, description: body.description ?? null,
    inputs: body.inputs.map(input => ({ ...input, sources: Array.isArray(input.sources) ? input.sources : [], required: input.required !== false })),
    outputs: body.outputs.map(output => ({ ...output, kind: output.kind === 'setpoint' ? 'setpoint' : 'value' })),
  };
}

export async function loadStarters(signal?: AbortSignal, fetcher: typeof fetch = fetch): Promise<Starter[]> {
  const body = await send('/authoring/starters', { signal }, fetcher);
  if (!Array.isArray(body)) throw new ConnectionsError('invalidResponse');
  return body as Starter[];
}

export async function validateDraft(draft: ModuleDraft, signal?: AbortSignal, fetcher: typeof fetch = fetch): Promise<Analysis> {
  const body = await send('/authoring/validate', { ...json('POST', draft), signal }, fetcher);
  if (!body || typeof body !== 'object' || !Array.isArray((body as Analysis).problems)) throw new ConnectionsError('invalidResponse');
  return body as Analysis;
}

export type EvaluateTarget = { connectionId?: string; externalId?: string; samples?: Record<string, string>; parameters?: Record<string, string> };

export async function evaluateDraft(draft: ModuleDraft, target: EvaluateTarget, fetcher: typeof fetch = fetch): Promise<ModuleRun> {
  const body = await send('/authoring/evaluate', json('POST', { draft, ...target }), fetcher);
  if (!body || typeof body !== 'object' || !Array.isArray((body as ModuleRun).inputs)) throw new ConnectionsError('invalidResponse');
  return body as ModuleRun;
}
