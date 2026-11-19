import { useState } from 'react';
import { ArrowDown, ArrowUp, Trash2 } from 'lucide-react';
import { ActionButton } from '../components/ActionButton';
import { CheckboxField, NumberField, SelectField, TextField } from '../components/FormFields';
import { Section } from '../components/Section';
import { t } from '../localization';
import {
  addCalculation,
  addRule,
  blankRecommendation,
  functionNames,
  moveRule,
  namesBefore,
  removeCalculation,
  removeRule,
  updateCalculation,
  updateRule,
} from './rulesModel';
import type { RuleRecommendation, RulesModel } from './rulesModel';

const numberOf = (value: string) => (value === '' ? null : Number(value));

/** An expression with the values and functions it may use, one click away. */
function ExpressionField({
  label,
  value,
  names,
  error,
  onChange,
}: {
  label: string;
  value: string;
  names: string[];
  error?: string;
  onChange: (next: string) => void;
}) {
  const insert = (word: string) => onChange(value + (value === '' || value.endsWith(' ') ? '' : ' ') + word);
  return (
    <div className="authoring-expression">
      <TextField
        label={label}
        value={value}
        maxLength={512}
        error={error}
        onChange={(event) => onChange(event.target.value)}
      />
      <div className="authoring-palette" role="group" aria-label={t('flow.palette')}>
        {names.map((name) => (
          <button key={name} type="button" className="authoring-chip" onClick={() => insert(name)}>
            {name}
          </button>
        ))}
        {functionNames.map((name) => (
          <button key={name} type="button" className="authoring-chip" data-function onClick={() => insert(name + '(')}>
            {name}()
          </button>
        ))}
      </div>
    </div>
  );
}

function AddByName({ label, action, onAdd }: { label: string; action: string; onAdd: (name: string) => void }) {
  const [name, setName] = useState('');
  return (
    <div className="authoring-add">
      <form
        className="authoring-flow-search"
        onSubmit={(event) => {
          event.preventDefault();
          const next = name.trim();
          if (!next) return;
          onAdd(next);
          setName('');
        }}
      >
        <TextField label={label} value={name} maxLength={64} onChange={(event) => setName(event.target.value)} />
        <ActionButton type="submit" label={action} disabled={!name.trim()} />
      </form>
    </div>
  );
}

/** Values worked out from inputs, settings and the calculations before them. */
export function CalculationsEditor({
  rules,
  inputNames,
  onChange,
}: {
  rules: RulesModel;
  inputNames: string[];
  onChange: (next: RulesModel) => void;
}) {
  return (
    <Section title={t('flow.calculationsTitle')}>
      {rules.calculations.length > 0 && (
        <ul className="authoring-flow-inputs">
          {rules.calculations.map((item, index) => (
            <li key={index} className="authoring-flow-input">
              <div className="authoring-card-head">
                <span className="authoring-tag">{t('flow.tagDerived')}</span>
                <strong>{item.id}</strong>
                {item.expression && <span className="authoring-card-unit">{'= ' + item.expression}</span>}
                <ActionButton
                  label={t('flow.removeCalculation')}
                  icon={Trash2}
                  iconOnly
                  variant="quiet"
                  onClick={() => onChange(removeCalculation(rules, index))}
                />
              </div>
              <div className="authoring-card-body">
                <TextField
                  label={t('flow.parameterUnit')}
                  value={item.unit ?? ''}
                  maxLength={32}
                  onChange={(event) => onChange(updateCalculation(rules, index, { unit: event.target.value || null }))}
                />
              </div>
              <ExpressionField
                label={t('flow.expression')}
                value={item.expression}
                names={namesBefore(rules, inputNames, index)}
                onChange={(next) => onChange(updateCalculation(rules, index, { expression: next }))}
              />
            </li>
          ))}
        </ul>
      )}
      <AddByName
        label={t('flow.parameterName')}
        action={t('flow.addCalculation')}
        onAdd={(name) => onChange(addCalculation(rules, name))}
      />
    </Section>
  );
}

function RecommendationFields({
  recommend,
  names,
  onChange,
}: {
  recommend: RuleRecommendation;
  names: string[];
  onChange: (next: RuleRecommendation) => void;
}) {
  const set = (patch: Partial<RuleRecommendation>) => onChange({ ...recommend, ...patch });
  return (
    <>
      <div className="authoring-card-body">
        <TextField
          label={t('flow.setpoint')}
          value={recommend.setpoint}
          maxLength={128}
          onChange={(event) => set({ setpoint: event.target.value })}
        />
        <TextField
          label={t('flow.parameterUnit')}
          value={recommend.unit ?? ''}
          maxLength={32}
          onChange={(event) => set({ unit: event.target.value || null })}
        />
      </div>
      <ExpressionField
        label={t('flow.suggestedValue')}
        value={recommend.valueExpression}
        names={names}
        onChange={(next) => set({ valueExpression: next })}
      />
      <div className="authoring-card-body">
        <SelectField
          label={t('flow.currentValue')}
          value={recommend.currentValue ?? ''}
          onChange={(event) => set({ currentValue: event.target.value || null })}
        >
          <option value="">{t('flow.noneChosen')}</option>
          {names.map((name) => (
            <option key={name} value={name}>
              {name}
            </option>
          ))}
        </SelectField>
        <NumberField
          label={t('flow.limitMinimum')}
          value={recommend.minimum ?? ''}
          step="any"
          onChange={(event) => set({ minimum: numberOf(event.target.value) })}
        />
        <NumberField
          label={t('flow.limitMaximum')}
          value={recommend.maximum ?? ''}
          step="any"
          onChange={(event) => set({ maximum: numberOf(event.target.value) })}
        />
        <NumberField
          label={t('flow.limitChange')}
          value={recommend.maximumChange ?? ''}
          step="any"
          min={0}
          onChange={(event) => set({ maximumChange: numberOf(event.target.value) })}
        />
      </div>
    </>
  );
}

/** Ordered rules: the first whose condition is true decides, with its reason and an optional setpoint proposal. */
export function RulesEditor({
  rules,
  inputNames,
  onChange,
}: {
  rules: RulesModel;
  inputNames: string[];
  onChange: (next: RulesModel) => void;
}) {
  const names = namesBefore(rules, inputNames, null);
  return (
    <Section title={t('flow.rulesTitle')}>
      {rules.rules.length > 0 && (
        <ul className="authoring-flow-inputs">
          {rules.rules.map((item, index) => (
            <li key={index} className="authoring-flow-input">
              <div className="authoring-card-head">
                <span className="authoring-tag">{t('flow.tagRule') + ' ' + (index + 1)}</span>
                <strong>{item.id}</strong>
                {item.outcomeCode && <span className="authoring-card-unit">{item.outcomeCode}</span>}
                <ActionButton
                  label={t('flow.moveUp')}
                  icon={ArrowUp}
                  iconOnly
                  variant="quiet"
                  disabled={index === 0}
                  onClick={() => onChange(moveRule(rules, index, -1))}
                />
                <ActionButton
                  label={t('flow.moveDown')}
                  icon={ArrowDown}
                  iconOnly
                  variant="quiet"
                  disabled={index === rules.rules.length - 1}
                  onClick={() => onChange(moveRule(rules, index, 1))}
                />
                <ActionButton
                  label={t('flow.removeRule')}
                  icon={Trash2}
                  iconOnly
                  variant="quiet"
                  onClick={() => onChange(removeRule(rules, index))}
                />
              </div>
              <ExpressionField
                label={t('flow.when')}
                value={item.when}
                names={names}
                onChange={(next) => onChange(updateRule(rules, index, { when: next }))}
              />
              <div className="authoring-card-body">
                <TextField
                  label={t('flow.reason')}
                  value={item.reason}
                  maxLength={500}
                  onChange={(event) => onChange(updateRule(rules, index, { reason: event.target.value }))}
                />
                <TextField
                  label={t('flow.outcomeCode')}
                  value={item.outcomeCode}
                  maxLength={128}
                  onChange={(event) =>
                    onChange(
                      updateRule(rules, index, { outcomeCode: event.target.value.toUpperCase().replace(/ /g, '_') }),
                    )
                  }
                />
                <TextField
                  label={t('flow.ruleTitle')}
                  value={item.title ?? ''}
                  maxLength={160}
                  onChange={(event) => onChange(updateRule(rules, index, { title: event.target.value || null }))}
                />
              </div>
              <CheckboxField
                label={t('flow.recommendSetpoint')}
                checked={item.recommend !== null}
                onChange={(event) =>
                  onChange(updateRule(rules, index, { recommend: event.target.checked ? blankRecommendation : null }))
                }
              />
              {item.recommend && (
                <RecommendationFields
                  recommend={item.recommend}
                  names={names}
                  onChange={(next) => onChange(updateRule(rules, index, { recommend: next }))}
                />
              )}
            </li>
          ))}
        </ul>
      )}
      <AddByName
        label={t('flow.parameterName')}
        action={t('flow.addRule')}
        onAdd={(name) => onChange(addRule(rules, name))}
      />
    </Section>
  );
}
