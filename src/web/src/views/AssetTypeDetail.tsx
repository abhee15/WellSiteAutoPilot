import type { AssetTypeResponse } from '../api/generated/v1/types.gen';
import { t } from '../localization';
import { StatusPill } from '../components/common';

interface SchemaField { name: string; type: string; rules: string; required: boolean }

/** Reads the supported JSON Schema subset into rows; anything unreadable yields no rows. */
export function schemaFields(json: string | null | undefined): SchemaField[] {
  try {
    const schema: unknown = JSON.parse(json ?? '{}');
    const root = schema as { properties?: Record<string, Record<string, unknown>>; required?: unknown };
    if (!root || typeof root.properties !== 'object' || root.properties === null) return [];
    const required = Array.isArray(root.required) ? root.required : [];
    return Object.entries(root.properties).map(([name, def]) => {
      const rules: string[] = [];
      if (Array.isArray(def?.enum)) rules.push(`one of ${def.enum.join(', ')}`);
      if (def?.minimum !== undefined) rules.push(`min ${String(def.minimum)}`);
      if (def?.maximum !== undefined) rules.push(`max ${String(def.maximum)}`);
      if (def?.minLength !== undefined) rules.push(`min length ${String(def.minLength)}`);
      if (def?.maxLength !== undefined) rules.push(`max length ${String(def.maxLength)}`);
      const title = typeof def?.title === 'string' ? def.title : '';
      return {
        name: title ? `${title} (${name})` : name,
        type: typeof def?.type === 'string' ? def.type : '',
        rules: rules.join('; '),
        required: required.includes(name),
      };
    });
  } catch {
    return [];
  }
}

export function AssetTypeDetail({ type, count, onShowAssets, onClose }: {
  type: AssetTypeResponse;
  count: number;
  onShowAssets: (typeName: string) => void;
  onClose: () => void;
}) {
  const fields = schemaFields(type.attributeSchemaJson);
  const name = type.displayName ?? type.key ?? '';
  return (
    <section className="split-type-detail" aria-label={t('assetTypes.details')}>
      <header>
        <h3>{name}</h3>
        <StatusPill value={type.isActive ? 'Active' : 'Inactive'} />
        <button type="button" className="secondary-button" onClick={onClose}>
          {t('assetTypes.closeDetails')}</button>
      </header>
      <p className="split-note">{type.key} · v{type.schemaVersion}</p>
      {count > 0 && <button type="button" className="secondary-button" onClick={() => onShowAssets(name)}>
        {t('assets.showOfType', { count, name })}</button>}
      <h4>{t('assetTypes.fields')}</h4>
      {fields.length === 0 ? <p className="split-note">{t('assetTypes.noFields')}</p> : (
        <div className="table-wrap">
          <table className="activity-table">
            <thead><tr>
              <th>{t('assetTypes.fieldName')}</th><th>{t('assetTypes.fieldType')}</th>
              <th>{t('assetTypes.fieldRule')}</th><th>{t('assetTypes.required')}</th>
            </tr></thead>
            <tbody>
              {fields.map(field => (
                <tr key={field.name}>
                  <td>{field.name}</td><td>{field.type}</td><td>{field.rules}</td>
                  <td>{field.required ? '✓' : ''}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <details>
        <summary>{t('assetTypes.rawSchema')}</summary>
        <pre>{type.attributeSchemaJson}</pre>
      </details>
    </section>
  );
}
