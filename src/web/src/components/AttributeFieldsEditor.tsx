import type { AttributeField, AttributeFieldError, AttributeValues } from '../views/assetAttributeForm';
import { t } from '../localization';
import './attributeFields.css';

type Props = {
  fields: readonly AttributeField[];
  values: AttributeValues;
  errors: Record<string, AttributeFieldError>;
  idPrefix: string;
  legend: string;
  disabled?: boolean;
  onChange: (key: string, value: string | boolean) => void;
  onUseJson: () => void;
};

/**
 * Typed fields generated from a JSON Schema (Asset Type attributes, module settings). Presentation
 * only: the server validates the same schema and stays the authority.
 */
export function AttributeFieldsEditor({
  fields, values, errors, idPrefix, legend, disabled, onChange, onUseJson,
}: Props) {
  return (
    <fieldset className="attribute-fields" disabled={disabled}>
      <legend>{legend}</legend>
      {fields.map(field => {
        const problem = errors[field.key];
        const id = idPrefix + field.key;
        const set = (value: string | boolean) => onChange(field.key, value);
        const label = field.label + (field.required ? '' : ' ' + t('assets.createOptional'));
        const common = { id, 'aria-invalid': problem ? true : undefined,
          'aria-describedby': problem ? id + '-error' : undefined } as const;
        return <div className="attribute-field" key={field.key}>
          {field.kind === 'boolean' ? (
            <label htmlFor={id}>
              <input {...common} type="checkbox" checked={values[field.key] === true}
                onChange={event => set(event.target.checked)} /> {label}
            </label>
          ) : (
            <>
              <label htmlFor={id}>{label}</label>
              {field.kind === 'choice' ? (
                <select {...common} value={String(values[field.key] ?? '')}
                  onChange={event => set(event.target.value)}>
                  <option value="">{t('assets.createChooseValue')}</option>
                  {field.options?.map(option => <option key={option} value={option}>{option}</option>)}
                </select>
              ) : (
                <input {...common} type="text"
                  inputMode={field.kind === 'text' ? undefined : 'decimal'}
                  value={String(values[field.key] ?? '')}
                  maxLength={field.maxLength}
                  onChange={event => set(event.target.value)} />
              )}
            </>
          )}
          {field.description && <small>{field.description}</small>}
          {problem && <small id={id + '-error'} className="attribute-field-error">
            {t(('assets.createField.' + problem) as 'assets.createField.required')}</small>}
        </div>;
      })}
      <button type="button" className="secondary-button" onClick={onUseJson}>
        {t('assets.createUseJson')}</button>
    </fieldset>
  );
}
