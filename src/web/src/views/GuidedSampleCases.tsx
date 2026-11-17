import type { GuidedModulePreviewResponse, GuidedSampleCaseRequest } from '../api/generated/v1/types.gen';
import { ActionButton } from '../components/ActionButton';
import { NumberField, SelectField, TextField } from '../components/FormFields';
import { t } from '../localization';

/** The expectation value meaning "the rule must not trigger"; any other value must be the rule's outcome code. */
export const noOutcome = 'NONE';
export const maximumSampleCases = 50;

/** Starting cases for a new draft. They carry no expectations: the author decides what each case must show. */
export function defaultSampleCases(pressure: boolean, boundary: number): GuidedSampleCaseRequest[] {
  const none = null;
  if (pressure)
    return [
      { name: t('guided.casePressureEqual'), casingPsi: 120, tubingPsi: 120, value: none, expectedOutcome: none },
      { name: t('guided.casePressureAtLimit'), casingPsi: boundary, tubingPsi: 0, value: none, expectedOutcome: none },
      {
        name: t('guided.casePressureAbove'),
        casingPsi: boundary + 1,
        tubingPsi: 0,
        value: none,
        expectedOutcome: none,
      },
      { name: t('guided.casePressureMissing'), casingPsi: none, tubingPsi: 120, value: none, expectedOutcome: none },
    ];
  return [
    {
      name: t('guided.caseBelow'),
      value: boundary === 0 ? 8 : Math.max(0, boundary - 8),
      casingPsi: none,
      tubingPsi: none,
      expectedOutcome: none,
    },
    { name: t('guided.caseAtBoundary'), value: boundary, casingPsi: none, tubingPsi: none, expectedOutcome: none },
    { name: t('guided.caseMissing'), value: none, casingPsi: none, tubingPsi: none, expectedOutcome: none },
  ];
}

function numberOrNull(text: string): number | null {
  return text.trim() === '' ? null : Number(text);
}

function expectationText(item: GuidedSampleCaseRequest, result: GuidedModulePreviewResponse | undefined) {
  if (!item.expectedOutcome || !result) return null;
  const observed = result.observedOutcome ?? null;
  if (observed === item.expectedOutcome) return t('guided.caseMatches');
  return t('guided.caseMismatch', {
    observed: observed === null ? t('guided.caseNotEvaluated') : observed,
  });
}

/**
 * Edits the named sample cases saved with a rule draft. Expectations are the author's assertions and are kept apart
 * from the observed outcome, which only ever comes from running the saved draft.
 */
export function GuidedSampleCases({
  cases,
  pressure,
  outcomeCode,
  results,
  disabled,
  onChange,
}: {
  cases: readonly GuidedSampleCaseRequest[];
  pressure: boolean;
  outcomeCode: string;
  results: readonly GuidedModulePreviewResponse[];
  disabled: boolean;
  onChange: (next: GuidedSampleCaseRequest[]) => void;
}) {
  function update(index: number, patch: Partial<GuidedSampleCaseRequest>) {
    onChange(cases.map((item, position) => (position === index ? { ...item, ...patch } : item)));
  }

  return (
    <section aria-label={t('guided.cases')}>
      <ul>
        {cases.map((item, index) => {
          const number = index + 1;
          const status = expectationText(item, results[index]);
          return (
            <li key={index}>
              <div className="guided-authoring-fields">
                <TextField
                  label={t('guided.caseName', { number })}
                  maxLength={60}
                  value={item.name ?? ''}
                  disabled={disabled}
                  onChange={(event) => update(index, { name: event.target.value })}
                />
                {pressure ? (
                  <>
                    <NumberField
                      label={t('guided.caseCasing', { number })}
                      min={0}
                      value={item.casingPsi ?? ''}
                      disabled={disabled}
                      onChange={(event) => update(index, { casingPsi: numberOrNull(event.target.value) })}
                    />
                    <NumberField
                      label={t('guided.caseTubing', { number })}
                      min={0}
                      value={item.tubingPsi ?? ''}
                      disabled={disabled}
                      onChange={(event) => update(index, { tubingPsi: numberOrNull(event.target.value) })}
                    />
                  </>
                ) : (
                  <NumberField
                    label={t('guided.caseValue', { number })}
                    min={0}
                    value={item.value ?? ''}
                    disabled={disabled}
                    onChange={(event) => update(index, { value: numberOrNull(event.target.value) })}
                  />
                )}
                <SelectField
                  label={t('guided.caseExpected', { number })}
                  value={item.expectedOutcome ?? ''}
                  disabled={disabled}
                  onChange={(event) => update(index, { expectedOutcome: event.target.value || null })}
                >
                  <option value="">{t('guided.caseNoExpectation')}</option>
                  <option value={outcomeCode}>{t('guided.caseExpectTrigger')}</option>
                  <option value={noOutcome}>{t('guided.caseExpectNone')}</option>
                </SelectField>
                <ActionButton
                  label={t('guided.caseRemove', { number })}
                  variant="quiet"
                  disabled={disabled || cases.length <= 1}
                  onClick={() => onChange(cases.filter((_, position) => position !== index))}
                />
              </div>
              {status && <p>{status}</p>}
            </li>
          );
        })}
      </ul>
      <ActionButton
        label={t('guided.caseAdd')}
        variant="quiet"
        disabled={disabled || cases.length >= maximumSampleCases}
        onClick={() =>
          onChange([
            ...cases,
            {
              name: t('guided.caseDefaultName', { number: cases.length + 1 }),
              value: null,
              casingPsi: null,
              tubingPsi: null,
              expectedOutcome: null,
            },
          ])
        }
      />
    </section>
  );
}
