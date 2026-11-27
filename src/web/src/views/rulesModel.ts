// The settings, calculations and rules of a module, as the server stores them. Only settings are edited so far;
// calculations and rules pass through untouched so nothing an author saved is lost.

interface RuleParameter {
  id: string;
  unit: string | null;
  default: number | null;
  minimum: number | null;
  maximum: number | null;
  assignable: boolean;
}

interface RuleCalculation {
  id: string;
  expression: string;
  unit: string | null;
}

export interface RuleRecommendation {
  setpoint: string;
  valueExpression: string;
  unit: string | null;
  currentValue: string | null;
  minimum: number | null;
  maximum: number | null;
  maximumChange: number | null;
  minimumFrom: string | null;
  maximumFrom: string | null;
  maximumChangeFrom: string | null;
  command: string | null;
  deadband: number | null;
  increment: number | null;
}

interface RuleItem {
  id: string;
  when: string;
  reason: string;
  outcomeCode: string;
  title: string | null;
  recommend: RuleRecommendation | null;
}

export interface RulesModel {
  parameters: RuleParameter[];
  calculations: RuleCalculation[];
  rules: RuleItem[];
}

export const emptyRules: RulesModel = { parameters: [], calculations: [], rules: [] };

const reservedNames = new Set([
  'true',
  'false',
  'and',
  'or',
  'not',
  'min',
  'max',
  'avg',
  'abs',
  'round',
  'clamp',
  'if',
]);
const identifier = /^[A-Za-z][A-Za-z0-9_]{0,63}$/;

const numberOrNull = (value: unknown) => (typeof value === 'number' ? value : null);
const textOrNull = (value: unknown) => (typeof value === 'string' && value !== '' ? value : null);
const textOf = (value: unknown) => (typeof value === 'string' ? value : '');
const listOf = (value: unknown) => (Array.isArray(value) ? (value as Record<string, unknown>[]) : []);

function recommendationOf(value: unknown): RuleRecommendation | null {
  if (typeof value !== 'object' || value === null) return null;
  const item = value as Record<string, unknown>;
  return {
    setpoint: textOf(item.setpoint),
    valueExpression: textOf(item.valueExpression),
    unit: textOrNull(item.unit),
    currentValue: textOrNull(item.currentValue),
    minimum: numberOrNull(item.minimum),
    maximum: numberOrNull(item.maximum),
    maximumChange: numberOrNull(item.maximumChange),
    minimumFrom: textOrNull(item.minimumFrom),
    maximumFrom: textOrNull(item.maximumFrom),
    maximumChangeFrom: textOrNull(item.maximumChangeFrom),
    command: textOrNull(item.command),
    deadband: numberOrNull(item.deadband),
    increment: numberOrNull(item.increment),
  };
}

export function parseRules(json: string | null | undefined): RulesModel {
  if (!json) return emptyRules;
  try {
    const value = JSON.parse(json) as Record<string, unknown>;
    const parameters = Array.isArray(value.parameters) ? (value.parameters as Record<string, unknown>[]) : [];
    return {
      parameters: parameters.map((item) => ({
        id: typeof item.id === 'string' ? item.id : '',
        unit: typeof item.unit === 'string' ? item.unit : null,
        default: numberOrNull(item.default),
        minimum: numberOrNull(item.minimum),
        maximum: numberOrNull(item.maximum),
        assignable: item.assignable === true,
      })),
      calculations: listOf(value.calculations).map((item) => ({
        id: textOf(item.id),
        expression: textOf(item.expression),
        unit: textOrNull(item.unit),
      })),
      rules: listOf(value.rules).map((item) => ({
        id: textOf(item.id),
        when: textOf(item.when),
        reason: textOf(item.reason),
        outcomeCode: textOf(item.outcomeCode),
        title: textOrNull(item.title),
        recommend: recommendationOf(item.recommend),
      })),
    };
  } catch {
    return emptyRules;
  }
}

/** Nothing authored means nothing to store. */
export function serializeRules(model: RulesModel): string | null {
  if (model.parameters.length === 0 && model.calculations.length === 0 && model.rules.length === 0) return null;
  return JSON.stringify(model);
}

export function addParameter(model: RulesModel, id: string): RulesModel {
  const parameter: RuleParameter = { id, unit: null, default: null, minimum: null, maximum: null, assignable: false };
  return { ...model, parameters: [...model.parameters, parameter] };
}

export function updateParameter(model: RulesModel, index: number, patch: Partial<RuleParameter>): RulesModel {
  return { ...model, parameters: model.parameters.map((item, at) => (at === index ? { ...item, ...patch } : item)) };
}

export function removeParameter(model: RulesModel, index: number): RulesModel {
  return { ...model, parameters: model.parameters.filter((_, at) => at !== index) };
}

export type ParameterProblem = 'name' | 'duplicate' | 'default' | 'range' | 'outside';

/** What stops a setting from being saved, by position. Settings that are fine are absent. */
export function parameterProblems(model: RulesModel, takenNames: string[]): Map<number, ParameterProblem> {
  const problems = new Map<number, ParameterProblem>();
  const seen = new Set(takenNames);
  model.parameters.forEach((item, index) => {
    if (!identifier.test(item.id) || reservedNames.has(item.id.toLowerCase())) problems.set(index, 'name');
    else if (seen.has(item.id)) problems.set(index, 'duplicate');
    else if (item.default === null) problems.set(index, 'default');
    else if (item.minimum !== null && item.maximum !== null && item.minimum > item.maximum)
      problems.set(index, 'range');
    else if (
      (item.minimum !== null && item.default < item.minimum) ||
      (item.maximum !== null && item.default > item.maximum)
    )
      problems.set(index, 'outside');
    seen.add(item.id);
  });
  return problems;
}

export const functionNames = ['min', 'max', 'avg', 'abs', 'round', 'clamp', 'if'];

export function addCalculation(model: RulesModel, id: string): RulesModel {
  return { ...model, calculations: [...model.calculations, { id, expression: '', unit: null }] };
}

export function updateCalculation(model: RulesModel, index: number, patch: Partial<RuleCalculation>): RulesModel {
  return {
    ...model,
    calculations: model.calculations.map((item, at) => (at === index ? { ...item, ...patch } : item)),
  };
}

export function removeCalculation(model: RulesModel, index: number): RulesModel {
  return { ...model, calculations: model.calculations.filter((_, at) => at !== index) };
}

export function addRule(model: RulesModel, id: string): RulesModel {
  const rule: RuleItem = { id, when: '', reason: '', outcomeCode: '', title: null, recommend: null };
  return { ...model, rules: [...model.rules, rule] };
}

export function updateRule(model: RulesModel, index: number, patch: Partial<RuleItem>): RulesModel {
  return { ...model, rules: model.rules.map((item, at) => (at === index ? { ...item, ...patch } : item)) };
}

export function removeRule(model: RulesModel, index: number): RulesModel {
  return { ...model, rules: model.rules.filter((_, at) => at !== index) };
}

export function moveRule(model: RulesModel, index: number, by: -1 | 1): RulesModel {
  const target = index + by;
  if (target < 0 || target >= model.rules.length) return model;
  const rules = [...model.rules];
  [rules[index], rules[target]] = [rules[target], rules[index]];
  return { ...model, rules };
}

export const blankRecommendation: RuleRecommendation = {
  setpoint: '',
  valueExpression: '',
  unit: null,
  currentValue: null,
  minimum: null,
  maximum: null,
  maximumChange: null,
  minimumFrom: null,
  maximumFrom: null,
  maximumChangeFrom: null,
  command: null,
  deadband: null,
  increment: null,
};

const outcomePattern = /^[A-Z][A-Z0-9_]{0,127}$/;

/** Names a calculation or rule may read, in the order they are worked out. */
export function namesBefore(model: RulesModel, inputNames: string[], calculationIndex: number | null): string[] {
  const calculations = model.calculations
    .slice(0, calculationIndex ?? model.calculations.length)
    .map((item) => item.id);
  return [...inputNames, ...model.parameters.map((item) => item.id), ...calculations].filter((name) => name !== '');
}

/** What is missing before the logic can be saved. The server checks the expressions themselves. */
export function logicIncomplete(model: RulesModel, takenNames: string[]): boolean {
  const names = new Set([...takenNames, ...model.parameters.map((item) => item.id)]);
  for (const item of model.calculations) {
    if (!identifier.test(item.id) || reservedNames.has(item.id.toLowerCase()) || names.has(item.id)) return true;
    if (item.expression.trim() === '') return true;
    names.add(item.id);
  }
  const ruleIds = new Set<string>();
  for (const rule of model.rules) {
    if (!identifier.test(rule.id) || ruleIds.has(rule.id)) return true;
    ruleIds.add(rule.id);
    if (rule.when.trim() === '' || rule.reason.trim() === '' || !outcomePattern.test(rule.outcomeCode)) return true;
    const recommend = rule.recommend;
    if (recommend && (recommend.setpoint.trim() === '' || recommend.valueExpression.trim() === '')) return true;
    if (recommend?.maximumChange != null && recommend.maximumChange <= 0 && !recommend.maximumChangeFrom) return true;
    if ((recommend?.maximumChange != null || recommend?.maximumChangeFrom) && !recommend.currentValue) return true;
    if (recommend?.deadband != null && (recommend.deadband <= 0 || !recommend.currentValue)) return true;
    if (recommend?.increment != null && recommend.increment <= 0) return true;
  }
  return false;
}
