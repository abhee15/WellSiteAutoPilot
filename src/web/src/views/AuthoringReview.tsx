import { Section } from '../components/Section';
import { t } from '../localization';
import { parseRules } from './rulesModel';

interface ScenarioEvidence {
  name: string;
  values: Record<string, number>;
  outcomeCode: string;
  ruleId: string | null;
  proposal: { setpoint: string; suggestedValue: number; unit: string | null; limited: boolean } | null;
}

function evidenceOf(json: string | null | undefined): ScenarioEvidence[] {
  if (!json) return [];
  try {
    const parsed: unknown = JSON.parse(json);
    return Array.isArray(parsed) ? (parsed as ScenarioEvidence[]) : [];
  } catch {
    return [];
  }
}

/** What a reviewer reads before approving a rules module: its rules, then what the author's scenarios did. */
export function RulesReview({ rulesJson, evidenceJson }: { rulesJson: string; evidenceJson?: string | null }) {
  const rules = parseRules(rulesJson);
  const scenarios = evidenceOf(evidenceJson);
  return (
    <>
      <Section title={t('review.rules')}>
        <ol className="authoring-trace">
          {rules.rules.map((rule) => (
            <li key={rule.id}>
              <span>{rule.id}</span> {rule.when} <span>{rule.outcomeCode + ' — ' + rule.reason}</span>
            </li>
          ))}
        </ol>
        {rules.calculations.length > 0 && (
          <p className="authoring-result-line">
            {rules.calculations.map((item) => item.id + ' = ' + item.expression).join(', ')}
          </p>
        )}
      </Section>
      <Section title={t('review.scenarios')}>
        <ul>
          {scenarios.map((item) => (
            <li key={item.name}>
              <strong>{item.name}</strong>
              {' · ' +
                Object.entries(item.values)
                  .map(([name, value]) => name + ' ' + value)
                  .join(', ') +
                ' → ' +
                (item.outcomeCode === 'NO_RULE_MATCHED' ? t('flow.testNoRule') : item.outcomeCode)}
              {item.proposal &&
                ' · ' +
                  item.proposal.setpoint +
                  ' ' +
                  item.proposal.suggestedValue +
                  (item.proposal.unit ? ' ' + item.proposal.unit : '') +
                  (item.proposal.limited ? ' (' + t('flow.testLimited') + ')' : '')}
            </li>
          ))}
        </ul>
      </Section>
    </>
  );
}
