import { useMemo, useState } from 'react';
import { AttributeFieldsEditor } from '../components/AttributeFieldsEditor';
import { t } from '../localization';
import { buildParameters, parameterFields, parameterValues } from './moduleParameterForm';
import type { AttributeValues } from './assetAttributeForm';

/** Partial module settings: omitted values inherit the revision defaults. Unsupported
 * structures stay in JSON; never populate inherited defaults as explicit overrides. */
export function ParameterOverridesEditor({ schema, value, label, context, idPrefix, onChange, onValidity }: {
  schema: unknown; value: string; label: string; context?: string; idPrefix: string;
  onChange: (json: string) => void; onValidity: (valid: boolean) => void;
}) {
  const fields = useMemo(() => parameterFields(schema, true), [schema]);
  const initial = useMemo(() => fields ? parameterValues(fields, value) : null, [fields, value]);
  const [values, setValues] = useState<AttributeValues | null>(null);
  const [json, setJson] = useState(false);
  const built = fields && values ? buildParameters(fields, values) : null;
  if (!fields || initial === null || json) return <label>{label}<textarea rows={2} maxLength={65536}
    value={value} onChange={event => { onValidity(true); onChange(event.target.value); }} /></label>;
  return <div>
    <p className="setup-step-hint">{t('draftEditor.inheritance')}</p>
    <AttributeFieldsEditor jsonLabel={t('draftEditor.editJson')} optionalBooleans fields={fields.map(field => ({ ...field, label: field.label + ' · ' + (context ?? label) }))} values={values ?? initial} errors={built?.errors ?? {}}
      idPrefix={idPrefix} legend={label} onUseJson={() => {
        if (built && Object.keys(built.errors).length) return;
        onChange(built?.json ?? value); onValidity(true); setJson(true);
      }} onChange={(key, next) => {
        const updated = { ...(values ?? initial), [key]: next };
        const result = buildParameters(fields, updated);
        setValues(updated); onValidity(Object.keys(result.errors).length === 0);
        if (Object.keys(result.errors).length === 0) onChange(result.json);
      }} />
  </div>;
}
