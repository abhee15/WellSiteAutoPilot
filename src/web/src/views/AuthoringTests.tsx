import { useState } from 'react';
import { previewGuidedModuleRules } from '../api/generated/v1/sdk.gen';
import type { GuidedRulesPreviewResponse } from '../api/generated/v1/types.gen';
import { ActionButton } from '../components/ActionButton';
import { NumberField } from '../components/FormFields';
import { Notice } from '../components/Notice';
import { Section } from '../components/Section';
import { t } from '../localization';
import type { RulesModel } from './rulesModel';

export interface TestInput {
  name: string;
  unit: string;
}

const detailOf = (error: unknown) =>
  typeof error === 'object' && error !== null && typeof (error as { detail?: unknown }).detail === 'string'
    ? (error as { detail: string }).detail
    : t('flow.testFailed');

/** Type values for the module's inputs, run the saved rules, and see what they would do and why. */
export function TestsEditor({
  draftId,
  revision,
  saved,
  inputs,
  rules,
}: {
  draftId: string | null;
  revision: number | null;
  saved: boolean;
  inputs: TestInput[];
  rules: RulesModel;
}) {
  const [values, setValues] = useState<Record<string, string>>({});
  const [settings, setSettings] = useState<Record<string, string>>({});
  const [result, setResult] = useState<GuidedRulesPreviewResponse | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const ready =
    inputs.length > 0 && inputs.every((input) => values[input.name] !== undefined && values[input.name] !== '');

  async function run() {
    if (!draftId || revision === null) return;
    setBusy(true);
    setMessage(null);
    try {
      const { data, error } = await previewGuidedModuleRules({
        path: { id: draftId },
        body: {
          expectedRevision: revision,
          values: inputs.map((input) => ({ name: input.name, value: Number(values[input.name]) })),
          settings: rules.parameters
            .filter((item) => (settings[item.id] ?? '') !== '')
            .map((item) => ({ name: item.id, value: Number(settings[item.id]) })),
        },
      });
      if (!data) throw error ?? new Error();
      setResult(data);
    } catch (error) {
      setResult(null);
      setMessage(detailOf(error));
    } finally {
      setBusy(false);
    }
  }

  if (!saved || rules.rules.length === 0)
    return (
      <Section title={t('flow.testsTitle')}>
        <Notice tone="info" announce={false}>
          {rules.rules.length === 0 ? t('flow.testNeedsRules') : t('flow.testNeedsSave')}
        </Notice>
      </Section>
    );

  const proposal = result?.proposal ?? null;
  return (
    <Section title={t('flow.testsTitle')}>
      <div className="authoring-flow-input">
        <div className="authoring-card-body">
          {inputs.map((input) => (
            <NumberField
              key={input.name}
              label={input.unit ? input.name + ' (' + input.unit + ')' : input.name}
              value={values[input.name] ?? ''}
              step="any"
              onChange={(event) => setValues((previous) => ({ ...previous, [input.name]: event.target.value }))}
            />
          ))}
          {rules.parameters.map((item) => (
            <NumberField
              key={item.id}
              label={item.id + (item.unit ? ' (' + item.unit + ')' : '')}
              value={settings[item.id] ?? ''}
              placeholder={item.default === null ? '' : String(item.default)}
              step="any"
              onChange={(event) => setSettings((previous) => ({ ...previous, [item.id]: event.target.value }))}
            />
          ))}
        </div>
        <div>
          <ActionButton
            label={t('flow.runTest')}
            variant="primary"
            busy={busy}
            disabled={!ready}
            onClick={() => void run()}
          />
        </div>
      </div>
      {message && <Notice tone="error">{message}</Notice>}
      {result && (
        <div className="authoring-flow-input" aria-live="polite">
          <div className="authoring-card-head">
            <span className="authoring-tag">{t('flow.testOutcome')}</span>
            <strong>{result.outcomeCode === 'NO_RULE_MATCHED' ? t('flow.testNoRule') : result.outcomeCode}</strong>
            {result.ruleId && <span className="authoring-card-unit">{result.ruleId}</span>}
          </div>
          {proposal && (
            <p className="authoring-result-line">
              <strong>{proposal.setpoint}</strong>
              {' → ' + proposal.suggestedValue + (proposal.unit ? ' ' + proposal.unit : '')}
              {proposal.limited && ' (' + t('flow.testLimited') + ' ' + proposal.requestedValue + ')'}
              {' — ' + proposal.reason}
            </p>
          )}
          {(result.calculations ?? []).length > 0 && (
            <p className="authoring-result-line">
              {(result.calculations ?? []).map((item) => item.name + ' = ' + item.value).join(', ')}
            </p>
          )}
          <ol className="authoring-trace">
            {(result.trace ?? []).map((step, index) => (
              <li key={index}>
                <span>{step.kind}</span> {step.id} <span>{step.detail}</span>
              </li>
            ))}
          </ol>
        </div>
      )}
    </Section>
  );
}
