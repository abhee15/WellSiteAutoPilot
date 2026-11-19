import type { GuidedExternalSourceResponse, GuidedSourceHintRequest } from '../api/generated/v1/types.gen';
import { CheckboxField, SelectField } from '../components/FormFields';
import { t } from '../localization';

/** The same shape and key order the server returns, so an unchanged hint never looks edited. */
export function normalizeHint(hint: GuidedSourceHintRequest): GuidedSourceHintRequest {
  return { inputId: hint.inputId, externalSource: hint.externalSource };
}

/**
 * Every input is read from the module's primary provider unless it is marked external. An external input names the
 * system it comes from; only the sources the primary provider allows are offered, and none means no choice is shown.
 */
export function GuidedSourceHintFields({
  inputId,
  externalSources,
  hint,
  disabled,
  onChange,
}: {
  inputId: string;
  externalSources: readonly GuidedExternalSourceResponse[];
  hint: GuidedSourceHintRequest | undefined;
  disabled: boolean;
  onChange: (hint: GuidedSourceHintRequest | null) => void;
}) {
  if (externalSources.length === 0) return null;
  const selected = hint?.externalSource ?? '';
  return (
    <>
      <CheckboxField
        label={t('guided.externalInput')}
        checked={hint !== undefined}
        disabled={disabled}
        onChange={(event) =>
          onChange(event.target.checked ? { inputId, externalSource: externalSources[0]?.key ?? '' } : null)
        }
      />
      {hint && (
        <SelectField
          label={t('guided.externalSource')}
          value={selected}
          disabled={disabled}
          onChange={(event) => onChange({ inputId, externalSource: event.target.value })}
        >
          {externalSources.map((source) => (
            <option key={source.key} value={source.key ?? ''}>
              {source.displayName ?? source.key}
            </option>
          ))}
        </SelectField>
      )}
    </>
  );
}
