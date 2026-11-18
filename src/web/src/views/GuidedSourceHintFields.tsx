import type { AdapterOperationsResponse, GuidedSourceHintRequest } from '../api/generated/v1/types.gen';
import { CheckboxField, SelectField } from '../components/FormFields';
import { Notice } from '../components/Notice';
import { t } from '../localization';

/** The same shape and key order the server returns, so an unchanged hint never looks edited. */
export function normalizeHint(hint: GuidedSourceHintRequest): GuidedSourceHintRequest {
  return {
    inputId: hint.inputId,
    adapterKey: hint.adapterKey ?? null,
    operationKey: hint.operationKey ?? null,
    required: hint.required === true,
    parametersJson: hint.parametersJson ?? null,
  };
}

/**
 * Where an input is read from: a provider and an operation, from the registry. Leaving both empty means any
 * connection that serves the quantity, which keeps the module portable. Requiring a source narrows where it can run.
 */
export function GuidedSourceHintFields({
  inputId,
  quantity,
  adapters,
  hint,
  disabled,
  onChange,
}: {
  inputId: string;
  quantity: string;
  adapters: readonly AdapterOperationsResponse[];
  hint: GuidedSourceHintRequest | undefined;
  disabled: boolean;
  onChange: (hint: GuidedSourceHintRequest | null) => void;
}) {
  const adapterKey = hint?.adapterKey ?? '';
  const operationKey = hint?.operationKey ?? '';
  const adapter = adapters.find((candidate) => candidate.adapterKey === adapterKey);
  // Operations that serve this quantity, plus provider-level reads that are not tied to one quantity.
  const operations = (adapter?.operations ?? []).filter(
    (operation) => !operation.quantity || operation.quantity === quantity,
  );

  function emit(nextAdapter: string, nextOperation: string, required: boolean, parametersJson: string | null) {
    if (nextAdapter === '' && nextOperation === '') {
      onChange(null);
      return;
    }
    onChange({
      inputId,
      adapterKey: nextAdapter === '' ? null : nextAdapter,
      operationKey: nextOperation === '' ? null : nextOperation,
      required,
      parametersJson,
    });
  }

  return (
    <>
      <SelectField
        label={t('guided.sourceProvider')}
        value={adapterKey}
        disabled={disabled}
        onChange={(event) => emit(event.target.value, '', hint?.required === true, null)}
      >
        <option value="">{t('guided.sourceAnyProvider')}</option>
        {adapters.map((candidate) => (
          <option key={candidate.adapterKey} value={candidate.adapterKey}>
            {candidate.displayName ?? candidate.adapterKey}
          </option>
        ))}
      </SelectField>
      <SelectField
        label={t('guided.sourceOperation')}
        value={operationKey}
        disabled={disabled || adapterKey === ''}
        onChange={(event) => emit(adapterKey, event.target.value, hint?.required === true, null)}
      >
        <option value="">{t('guided.sourceAnyOperation')}</option>
        {operations.map((operation) => (
          <option key={operation.key} value={operation.key}>
            {operation.displayName ?? operation.key}
          </option>
        ))}
      </SelectField>
      <CheckboxField
        label={t('guided.sourceRequired')}
        checked={hint?.required === true}
        disabled={disabled || (adapterKey === '' && operationKey === '')}
        onChange={(event) => emit(adapterKey, operationKey, event.target.checked, hint?.parametersJson ?? null)}
      />
      {hint?.required === true && (
        <Notice tone="info" announce={false}>
          {t('guided.sourceRequiredWarning')}
        </Notice>
      )}
    </>
  );
}
