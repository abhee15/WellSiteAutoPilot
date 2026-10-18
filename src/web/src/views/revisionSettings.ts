import { attributeFields } from './assetAttributeForm';

export interface SettingLine { label: string; value: string }

/**
 * Plain-language lines for the settings saved on a revision, using the labels from the module's
 * own settings schema when it has one. Returns null for "nothing to show"; unreadable or unlabelled
 * settings fall back to their raw names so nothing is hidden.
 */
export function describeSettings(
  parametersJson: string | null | undefined,
  moduleManifestJson: string | null | undefined,
): SettingLine[] | null {
  let values: unknown;
  try { values = JSON.parse(parametersJson ?? ''); } catch { return null; }
  if (!values || typeof values !== 'object' || Array.isArray(values)) return null;
  const entries = Object.entries(values as Record<string, unknown>);
  if (entries.length === 0) return null;

  let labels = new Map<string, string>();
  try {
    const schema = (JSON.parse(moduleManifestJson ?? '') as { parameterSchema?: unknown }).parameterSchema;
    const fields = schema && typeof schema === 'object' ? attributeFields(JSON.stringify(schema)) : null;
    if (fields) labels = new Map(fields.map(field => [field.key, field.label]));
  } catch { /* no usable manifest: show the raw names */ }

  return entries.map(([key, value]) => ({
    label: labels.get(key) ?? key,
    value: typeof value === 'string' ? value
      : typeof value === 'boolean' ? (value ? 'Yes' : 'No')
        : typeof value === 'number' ? String(value)
          : JSON.stringify(value),
  }));
}
