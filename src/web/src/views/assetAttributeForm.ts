/**
 * Turns an Asset Type's attribute schema into typed form fields. Only flat objects of simple
 * properties are generated; anything else returns null and the page keeps the JSON editor.
 * The server stays the authority: this only helps people fill in values correctly.
 */
export type AttributeFieldKind = 'text' | 'integer' | 'number' | 'boolean' | 'choice';

export interface AttributeField {
  key: string;
  label: string;
  kind: AttributeFieldKind;
  required: boolean;
  description?: string;
  options?: string[];
  minimum?: number;
  maximum?: number;
  minLength?: number;
  maxLength?: number;
}

export type AttributeFieldError = 'required' | 'integer' | 'number' | 'minimum' | 'maximum' | 'length';
export type AttributeValues = Record<string, string | boolean | undefined>;

const humanize = (key: string) => {
  const spaced = key.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/[_-]+/g, ' ').trim();
  return spaced ? spaced.charAt(0).toUpperCase() + spaced.slice(1) : key;
};
const num = (value: unknown) => (typeof value === 'number' && Number.isFinite(value) ? value : undefined);

export function attributeFields(schemaJson: string | null | undefined): AttributeField[] | null {
  let schema: unknown;
  try { schema = JSON.parse(schemaJson ?? ''); } catch { return null; }
  if (!schema || typeof schema !== 'object') return null;
  const root = schema as { type?: unknown; properties?: unknown; required?: unknown };
  if (root.type !== 'object' || !root.properties || typeof root.properties !== 'object') return null;
  const required = new Set(Array.isArray(root.required) ? root.required.filter(x => typeof x === 'string') : []);
  const fields: AttributeField[] = [];
  for (const [key, raw] of Object.entries(root.properties as Record<string, unknown>)) {
    if (!raw || typeof raw !== 'object') return null;
    const property = raw as Record<string, unknown>;
    const type = property.type;
    let kind: AttributeFieldKind;
    let options: string[] | undefined;
    if (type === 'string' && Array.isArray(property.enum)) {
      if (!property.enum.every(x => typeof x === 'string')) return null;
      kind = 'choice'; options = property.enum as string[];
    } else if (type === 'string') kind = 'text';
    else if (type === 'integer') kind = 'integer';
    else if (type === 'number') kind = 'number';
    else if (type === 'boolean') kind = 'boolean';
    else return null; // nested objects, arrays and nulls are left to the JSON editor
    fields.push({
      key,
      label: typeof property.title === 'string' && property.title ? property.title : humanize(key),
      kind, required: required.has(key), options,
      description: typeof property.description === 'string' ? property.description : undefined,
      minimum: num(property.minimum), maximum: num(property.maximum),
      minLength: num(property.minLength), maxLength: num(property.maxLength),
    });
  }
  return fields.length > 0 ? fields : null;
}

/** The JSON text to send plus any per-field problems. Empty optional fields are left out. */
export function buildAttributes(
  fields: readonly AttributeField[],
  values: AttributeValues,
): { json: string; errors: Record<string, AttributeFieldError> } {
  const out: Record<string, string | number | boolean> = {};
  const errors: Record<string, AttributeFieldError> = {};
  for (const field of fields) {
    const raw = values[field.key];
    if (field.kind === 'boolean') {
      if (typeof raw === 'boolean') out[field.key] = raw;
      else if (field.required) out[field.key] = false;
      continue;
    }
    const text = typeof raw === 'string' ? raw.trim() : '';
    if (text === '') { if (field.required) errors[field.key] = 'required'; continue; }
    if (field.kind === 'integer' || field.kind === 'number') {
      const parsed = Number(text);
      if (!Number.isFinite(parsed)) { errors[field.key] = 'number'; continue; }
      if (field.kind === 'integer' && !Number.isInteger(parsed)) { errors[field.key] = 'integer'; continue; }
      if (field.minimum !== undefined && parsed < field.minimum) { errors[field.key] = 'minimum'; continue; }
      if (field.maximum !== undefined && parsed > field.maximum) { errors[field.key] = 'maximum'; continue; }
      out[field.key] = parsed;
    } else if (field.kind === 'choice') {
      if (!field.options?.includes(text)) { errors[field.key] = 'required'; continue; }
      out[field.key] = text;
    } else {
      if ((field.minLength !== undefined && text.length < field.minLength) ||
          (field.maxLength !== undefined && text.length > field.maxLength)) { errors[field.key] = 'length'; continue; }
      out[field.key] = text;
    }
  }
  return { json: JSON.stringify(out), errors };
}
