// The settings, calculations and rules of a module, as the server stores them. Only settings are edited so far;
// calculations and rules pass through untouched so nothing an author saved is lost.

export interface RuleParameter {
  id: string;
  unit: string | null;
  default: number | null;
  minimum: number | null;
  maximum: number | null;
  assignable: boolean;
}

export interface RulesModel {
  parameters: RuleParameter[];
  calculations: unknown[];
  rules: unknown[];
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
      calculations: Array.isArray(value.calculations) ? value.calculations : [],
      rules: Array.isArray(value.rules) ? value.rules : [],
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
