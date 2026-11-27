import { useState } from 'react';
import { previewGuidedModuleRules } from '../api/generated/v1/sdk.gen';
import type { GuidedRulesPreviewResponse } from '../api/generated/v1/types.gen';
import { ActionButton } from '../components/ActionButton';
import { NumberField, SelectField } from '../components/FormFields';
import { Notice } from '../components/Notice';
import { Section } from '../components/Section';
import { t } from '../localization';
import type { RulesModel } from './rulesModel';

/** A run the author kept: what went in and the outcome it gave, which submission checks again. */
export interface Scenario {
  name: string;
  values: { name: string; value: number }[];
  settings: { name: string; value: number }[];
  expectedOutcome: string;
}

interface TestInput {
  name: string;
  unit: string;
}

export function parseScenarios(json: string | null | undefined): Scenario[] {
  if (!json) return [];
  try {
    const value: unknown = JSON.parse(json);
    return Array.isArray(value) ? (value as Scenario[]) : [];
  } catch {
    return [];
  }
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
  scenarios,
  onAddScenario,
  onRemoveScenario,
  onChangeExpected,
}: {
  draftId: string | null;
  revision: number | null;
  saved: boolean;
  inputs: TestInput[];
  rules: RulesModel;
  scenarios: Scenario[];
  onAddScenario: (scenario: Scenario) => void;
  onRemoveScenario: (index: number) => void;
  onChangeExpected: (index: number, expectedOutcome: string) => void;
}) {
  const [values, setValues] = useState<Record<string, string>>({});
  const [settings, setSettings] = useState<Record<string, string>>({});
  const [result, setResult] = useState<GuidedRulesPreviewResponse | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [ran, setRan] = useState<Omit<Scenario, 'name'> | null>(null);

  const ready =
    inputs.length > 0 && inputs.every((input) => values[input.name] !== undefined && values[input.name] !== '');

  async function run() {
    if (!draftId || revision === null) return;
    setBusy(true);
    setMessage(null);
    try {
      const sent = {
        values: inputs.map((input) => ({ name: input.name, value: Number(values[input.name]) })),
        settings: rules.parameters
          .filter((item) => (settings[item.id] ?? '') !== '')
          .map((item) => ({ name: item.id, value: Number(settings[item.id]) })),
      };
      const { data, error } = await previewGuidedModuleRules({
        path: { id: draftId },
        body: { expectedRevision: revision, ...sent },
      });
      if (!data) throw error ?? new Error();
      setResult(data);
      setRan({
        ...sent,
        expectedOutcome: data.outcomeCode === 'NO_RULE_MATCHED' ? 'NONE' : (data.outcomeCode ?? 'NONE'),
      });
    } catch (error) {
      setResult(null);
      setRan(null);
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
      {scenarios.length > 0 && (
        <div className="authoring-flow-input">
          <div className="authoring-card-head">
            <span className="authoring-tag">{t('flow.scenarios')}</span>
          </div>
          {scenarios.map((item, index) => (
            <div key={item.name} className="authoring-card-head">
              <strong>{item.name}</strong>
              <span className="authoring-card-unit">
                {item.values.map((value) => value.name + ' ' + value.value).join(', ')}
              </span>
              <SelectField
                label={t('flow.scenarioExpects')}
                value={item.expectedOutcome}
                onChange={(event) => onChangeExpected(index, event.target.value)}
              >
                <option value="NONE">{t('flow.testNoRule')}</option>
                {[...new Set(rules.rules.map((rule) => rule.outcomeCode))].map((code) => (
                  <option key={code} value={code}>
                    {code}
                  </option>
                ))}
              </SelectField>
              <ActionButton iconOnly label={t('flow.removeScenario')} onClick={() => onRemoveScenario(index)} />
            </div>
          ))}
        </div>
      )}
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
          {ran && (
            <div>
              <ActionButton
                label={t('flow.addScenario')}
                onClick={() => {
                  onAddScenario({ name: t('flow.scenarioName') + ' ' + (scenarios.length + 1), ...ran });
                  setRan(null);
                }}
              />
            </div>
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
