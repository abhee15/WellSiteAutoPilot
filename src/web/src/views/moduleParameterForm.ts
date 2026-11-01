import { attributeFields, attributeValues, buildAttributes } from './assetAttributeForm';
import type { AttributeField, AttributeValues } from './assetAttributeForm';

/** Reuse scalar field controls for nested module objects. Arrays, conditional schemas,
 * and values the form cannot round-trip remain in the complete JSON editor. */
export type ParameterField = AttributeField & { path: string[] };
export function parameterFields(schema: unknown, overrides = false): ParameterField[] | null {
  const result: ParameterField[] = [];
  const walk = (raw: unknown, path: string[], labels: string[], parentRequired: boolean): boolean => {
    if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return false;
    const node = raw as Record<string, unknown>;
    if (node.type !== 'object' || !node.properties || typeof node.properties !== 'object' || Array.isArray(node.properties) ||
      ['allOf', 'anyOf', 'oneOf', 'if', '$ref', 'dependentSchemas', 'patternProperties'].some(key => key in node)) return false;
    const required = Array.isArray(node.required) ? node.required : [];
    for (const [key, property] of Object.entries(node.properties)) {
      if (['__proto__', 'constructor', 'prototype'].includes(key) || !property || typeof property !== 'object' || Array.isArray(property)) return false;
      const child = property as Record<string, unknown>;
      const fullPath = [...path, key];
      const label = typeof child.title === 'string' ? child.title : key.replace(/([a-z0-9])([A-Z])/g, '$1 $2');
      const isRequired = parentRequired && required.includes(key);
      if (child.type === 'object') {
        if (!walk(child, fullPath, [...labels, label], isRequired)) return false;
      } else {
        const fields = attributeFields(JSON.stringify({ type: 'object', properties: { [key]: child }, required: isRequired ? [key] : [] }));
        if (!fields) return false;
        const field = fields[0];
        result.push({ ...field, key: path.length ? JSON.stringify(fullPath) : key,
          path: fullPath, label: [...labels, field.label].join(' · '), required: !overrides && field.required });
      }
    }
    return true;
  };
  return walk(schema, [], [], true) && result.length > 0 ? result : null;
}
const object = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);
const canonical = (value: unknown): string => JSON.stringify(value, (_key, item: unknown) => object(item)
  ? Object.fromEntries(Object.entries(item).sort(([a], [b]) => a.localeCompare(b))) : item);
// JSON.parse accepts duplicate names. Keep those documents raw so the server can
// reject them rather than silently converting a user's text to last-value-wins.
function duplicateKeys(json: string): boolean {
  const stack: { object: boolean; key: boolean; names: Set<string> }[] = [];
  for (const token of json.match(/"(?:\\.|[^"\\])*"|[{}[\],]/g) ?? []) {
    if (token === '{' || token === '[') stack.push({ object: token === '{', key: true, names: new Set() });
    else if (token === '}' || token === ']') stack.pop();
    else {
      const current = stack.at(-1);
      if (token === ',') { if (current?.object) current.key = true; }
      else if (token.startsWith('"') && current?.object && current.key) {
        const name = JSON.parse(token) as string;
        if (current.names.has(name)) return true;
        current.names.add(name); current.key = false;
      }
    }
  }
  return false;
}
export function parameterValues(fields: readonly ParameterField[], json: string): AttributeValues | null {
  try {
    const original: unknown = JSON.parse(json);
    if (duplicateKeys(json)) return null;
    if (!object(original)) return null;
    const flat: Record<string, unknown> = {};
    for (const field of fields) {
      let value: unknown = original;
      for (const key of field.path) value = object(value) ? value[key] : undefined;
      if (value !== undefined) flat[field.key] = value;
    }
    const values = attributeValues(fields, JSON.stringify(flat));
    if (!values) return null;
    // Do not lose unmodeled values, empty objects, nulls or unsupported enum values.
    const rebuilt = buildParameters(fields, values);
    if (Object.keys(rebuilt.errors).length || canonical(JSON.parse(rebuilt.json)) !== canonical(original)) return null;
    return values;
  } catch { return null; }
}
export function buildParameters(fields: readonly ParameterField[], values: AttributeValues) {
  const flat = buildAttributes(fields, values);
  const parsed = JSON.parse(flat.json) as Record<string, unknown>;
  const nested: Record<string, unknown> = {};
  for (const field of fields) {
    if (!(field.key in parsed)) continue;
    let target = nested;
    for (const key of field.path.slice(0, -1)) {
      if (!object(target[key])) Object.defineProperty(target, key, { value: {}, enumerable: true, configurable: true, writable: true });
      target = target[key] as Record<string, unknown>;
    }
    Object.defineProperty(target, field.path[field.path.length - 1], { value: parsed[field.key], enumerable: true, configurable: true, writable: true });
  }
  return { ...flat, json: JSON.stringify(nested) };
}
