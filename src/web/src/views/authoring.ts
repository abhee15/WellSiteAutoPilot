import { ConnectionsError, json, send } from './connections';
import type { SetPointPlan } from './connections';

export type InputHistory = { daysBack: number; atLocal: string; windowMinutes?: number; reduce?: string };
export type DraftInput = {
  history?: InputHistory | null;
  name: string;
  quantity: string;
  unit?: string | null;
  basis?: string | null;
  maxAgeSeconds?: number | null;
  allowUncertain?: boolean;
  required: boolean;
  role?: string | null;
  label?: string | null;
  validMin?: number | null;
  validMax?: number | null;
};
export type DraftParameter = {
  name: string;
  value: string;
  unit?: string | null;
  label?: string | null;
  min?: number | null;
  max?: number | null;
};
export type DraftRule = { when: string; then: string; reason?: string | null };
export type DraftStep = { name: string; expression: string; label?: string | null; rules?: DraftRule[] | null };
export type OutputLimits = { min?: number | null; max?: number | null; maxStep?: number | null };
export type DraftOutput = {
  name: string;
  expression: string;
  kind: 'value' | 'setpoint' | 'recommendation';
  unit?: string | null;
  target?: string | null;
  limits?: OutputLimits | null;
  when?: string | null;
  label?: string | null;
  code?: string | null;
  title?: string | null;
  detail?: string | null;
};
export type TestCase = {
  name: string;
  inputs: Record<string, string>;
  expect: Record<string, string>;
  parameters?: Record<string, string> | null;
  tolerance?: number | null;
};
export type ModuleDraft = {
  name: string;
  description: string | null;
  inputs: DraftInput[];
  parameters: DraftParameter[];
  steps: DraftStep[];
  outputs: DraftOutput[];
  tests?: TestCase[] | null;
  moduleId?: string | null;
  version?: string;
  schemaVersion?: number;
  languageVersion?: number;
};
export type BindingSource = { input: string; basis?: string | null };
export type ModuleBindings = {
  inputs?: Record<string, BindingSource[]> | null;
  targets?: Record<string, string> | null;
};
export type Starter = {
  id: string;
  title: string;
  summary: string;
  draft: ModuleDraft;
  bindings?: ModuleBindings | null;
  teaches?: string[] | null;
};

export type Problem = { severity: 'Error' | 'Warning'; node: string; message: string };
export type GraphNode = {
  name: string;
  kind: 'input' | 'parameter' | 'step' | 'output';
  dependsOn: string[];
  usedBy: string[];
};
export type Analysis = {
  valid: boolean;
  problems: Problem[];
  graph: GraphNode[];
  order: string[];
  fingerprint: string;
};

export type Val = {
  kind: 'Number' | 'Flag' | 'Text' | 'Unavailable';
  number: number | null;
  flag: boolean | null;
  text: string | null;
  reason: string | null;
  unit?: string | null;
  asOf?: string | null;
  quality?: 'Good' | 'Uncertain' | 'Bad';
};
export type InputTrace = {
  name: string;
  resolution: string;
  value: Val;
  unit: string | null;
  source: string | null;
  basis: string | null;
  usedInput: string | null;
  reasons: string[];
  candidates: { source: string; basis: string; description: string }[];
  required: boolean;
  ageSeconds?: number | null;
  maxAgeSeconds?: number | null;
  timestampKind?: string | null;
  quantity?: string | null;
};
export type RowTrace = { when: string; state: 'Pass' | 'Fail' | 'Unknown'; decided: boolean; reason?: string | null };
export type NodeResult = {
  name: string;
  value: Val;
  expression: string | null;
  dependsOn: string[];
  matched?: string | null;
  rows?: RowTrace[] | null;
};
export type ProposalCheck = { name: string; status: 'Pass' | 'Fail' | 'Hold' | 'Info'; detail: string };
export type OutputResult = {
  name: string;
  kind: string;
  value: Val;
  unit: string | null;
  target: string | null;
  expression: string;
  plan: SetPointPlan | null;
  note: string | null;
  checks?: ProposalCheck[] | null;
  proposed?: boolean | null;
  advice?: { code: string; title: string; detail: string } | null;
};
export type ModuleOutcome = 'Runs' | 'Skipped' | 'NeedsSetup' | 'Unavailable' | 'Invalid';
export type ModuleRun = {
  outcome: ModuleOutcome;
  reasons: string[];
  fingerprint: string;
  inputs: InputTrace[];
  parameters: NodeResult[];
  steps: NodeResult[];
  outputs: OutputResult[];
  problems: Problem[];
  evaluatedAtUtc?: string | null;
  mode?: string;
  externalId?: string | null;
};

export type TestCheck = { output: string; expected: string; actual: string; passed: boolean };
export type TestCaseResult = { name: string; passed: boolean; checks: TestCheck[]; error?: string | null };
export type VerifyResult = {
  tests: { fingerprint: string; passed: number; failed: number; cases: TestCaseResult[] };
  manifestValid: boolean;
  manifestError: string | null;
};

export function showVal(value: Val, unit?: string | null): string {
  if (value.kind === 'Unavailable') return `unavailable (${value.reason ?? 'no value'})`;
  const text =
    value.kind === 'Number'
      ? String(Number((value.number ?? 0).toFixed(5)))
      : value.kind === 'Flag'
        ? value.flag
          ? 'true'
          : 'false'
        : (value.text ?? '');
  const shown = unit ?? value.unit;
  return shown ? `${text} ${shown}` : text;
}

const rawVal = (value: Val): string =>
  value.kind === 'Number'
    ? String(Number((value.number ?? 0).toFixed(5)))
    : value.kind === 'Flag'
      ? value.flag
        ? 'true'
        : 'false'
      : (value.text ?? '');

/** Turns a run into a stored example: the values every input had, and what each output did. Null when an input had no value to store. */
export function exampleFromRun(run: ModuleRun, name: string): TestCase | null {
  if (run.inputs.some((input) => input.required && input.value.kind === 'Unavailable')) return null;
  const inputs: Record<string, string> = {};
  for (const input of run.inputs) if (input.value.kind !== 'Unavailable') inputs[input.name] = rawVal(input.value);
  const expect: Record<string, string> = {};
  for (const output of run.outputs)
    expect[output.name] =
      output.kind === 'setpoint' && !output.proposed
        ? 'unavailable'
        : output.value.kind === 'Unavailable'
          ? 'unavailable'
          : rawVal(output.value);
  return { name, inputs, expect };
}

/** "2 min ago" style age for evidence, so an author sees at a glance how old a reading was. */
export function showAge(seconds: number | null | undefined): string {
  if (seconds === null || seconds === undefined) return 'age unknown';
  if (seconds < 90) return `${Math.max(0, Math.round(seconds))} s old`;
  if (seconds < 5400) return `${Math.round(seconds / 60)} min old`;
  if (seconds < 172800) return `${Math.round(seconds / 3600)} h old`;
  return `${Math.round(seconds / 86400)} d old`;
}

function isDraft(body: unknown): body is ModuleDraft {
  return (
    !!body &&
    typeof body === 'object' &&
    Array.isArray((body as ModuleDraft).inputs) &&
    Array.isArray((body as ModuleDraft).outputs) &&
    Array.isArray((body as ModuleDraft).steps) &&
    Array.isArray((body as ModuleDraft).parameters)
  );
}

/** Checks an imported or pasted description before it replaces the draft being edited. */
export function parseDraft(text: string): ModuleDraft {
  let body: unknown;
  try {
    body = JSON.parse(text);
  } catch {
    throw new ConnectionsError('invalid');
  }
  if (!isDraft(body) || typeof body.name !== 'string') throw new ConnectionsError('invalid');
  return {
    ...body,
    description: body.description ?? null,
    inputs: body.inputs.map((input) => ({
      ...input,
      quantity: input.quantity ?? '',
      required: input.required !== false,
    })),
    outputs: body.outputs.map((output) => ({
      ...output,
      kind: output.kind === 'setpoint' || output.kind === 'recommendation' ? output.kind : 'value',
    })),
  };
}

/** Identity of everything a run depends on, so a result can be matched to the request that produced it. */
export function requestKey(draft: ModuleDraft, target: EvaluateTarget): string {
  return JSON.stringify([
    draft,
    target.connectionId ?? null,
    target.externalId ?? null,
    target.samples ?? null,
    target.parameters ?? null,
    target.bindings ?? null,
  ]);
}

export async function loadStarters(signal?: AbortSignal, fetcher: typeof fetch = fetch): Promise<Starter[]> {
  const body = await send('/authoring/starters', { signal }, fetcher);
  if (!Array.isArray(body)) throw new ConnectionsError('invalidResponse');
  return body as Starter[];
}

export async function validateDraft(
  draft: ModuleDraft,
  signal?: AbortSignal,
  fetcher: typeof fetch = fetch,
): Promise<Analysis> {
  const body = await send('/authoring/validate', { ...json('POST', draft), signal }, fetcher);
  if (!body || typeof body !== 'object' || !Array.isArray((body as Analysis).problems))
    throw new ConnectionsError('invalidResponse');
  return body as Analysis;
}

export async function verifyDraft(
  draft: ModuleDraft,
  signal?: AbortSignal,
  fetcher: typeof fetch = fetch,
): Promise<VerifyResult> {
  const body = await send('/authoring/verify', { ...json('POST', draft), signal }, fetcher);
  if (!body || typeof body !== 'object' || !(body as VerifyResult).tests) throw new ConnectionsError('invalidResponse');
  return body as VerifyResult;
}

export type EvaluateTarget = {
  connectionId?: string;
  externalId?: string;
  samples?: Record<string, string>;
  parameters?: Record<string, string>;
  bindings?: ModuleBindings;
};

export async function evaluateDraft(
  draft: ModuleDraft,
  target: EvaluateTarget,
  fetcher: typeof fetch = fetch,
): Promise<ModuleRun> {
  const body = await send('/authoring/evaluate', json('POST', { draft, ...target }), fetcher);
  if (!body || typeof body !== 'object' || !Array.isArray((body as ModuleRun).inputs))
    throw new ConnectionsError('invalidResponse');
  return body as ModuleRun;
}
