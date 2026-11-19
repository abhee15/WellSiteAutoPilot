import { useState } from 'react';
import { Trash2 } from 'lucide-react';
import { ActionButton } from '../components/ActionButton';
import { CheckboxField, NumberField, TextField } from '../components/FormFields';
import { Section } from '../components/Section';
import { t } from '../localization';
import { addParameter, parameterProblems, removeParameter, updateParameter } from './rulesModel';
import type { ParameterProblem, RulesModel } from './rulesModel';

const problemCopy: Record<
  ParameterProblem,
  'flow.problemName' | 'flow.problemDuplicate' | 'flow.problemDefault' | 'flow.problemRange' | 'flow.problemOutside'
> = {
  name: 'flow.problemName',
  duplicate: 'flow.problemDuplicate',
  default: 'flow.problemDefault',
  range: 'flow.problemRange',
  outside: 'flow.problemOutside',
};

const numberOf = (value: string) => (value === '' ? null : Number(value));

/** Settings of the module: numbers an author chooses once, optionally changeable when the module is assigned. */
export function ParametersEditor({
  rules,
  takenNames,
  onChange,
}: {
  rules: RulesModel;
  takenNames: string[];
  onChange: (next: RulesModel) => void;
}) {
  const [name, setName] = useState('');
  const problems = parameterProblems(rules, takenNames);

  return (
    <Section title={t('flow.parametersTitle')}>
      {rules.parameters.length > 0 && (
        <ul className="authoring-flow-inputs">
          {rules.parameters.map((item, index) => {
            const problem = problems.get(index);
            return (
              <li key={index} className="authoring-flow-input">
                <div className="authoring-card-head">
                  <span className="authoring-tag">{t('flow.tagSetting')}</span>
                  <strong>{item.id || t('flow.unnamed')}</strong>
                  {item.default !== null && (
                    <span className="authoring-card-unit">{item.default + (item.unit ? ' ' + item.unit : '')}</span>
                  )}
                  <ActionButton
                    label={t('flow.removeParameter')}
                    icon={Trash2}
                    iconOnly
                    variant="quiet"
                    onClick={() => onChange(removeParameter(rules, index))}
                  />
                </div>
                <div className="authoring-card-body">
                  <TextField
                    label={t('flow.parameterName')}
                    value={item.id}
                    maxLength={64}
                    error={problem === 'name' || problem === 'duplicate' ? t(problemCopy[problem]) : undefined}
                    onChange={(event) => onChange(updateParameter(rules, index, { id: event.target.value.trim() }))}
                  />
                  <NumberField
                    label={t('flow.parameterDefault')}
                    value={item.default ?? ''}
                    step="any"
                    error={problem === 'default' || problem === 'outside' ? t(problemCopy[problem]) : undefined}
                    onChange={(event) =>
                      onChange(updateParameter(rules, index, { default: numberOf(event.target.value) }))
                    }
                  />
                  <TextField
                    label={t('flow.parameterUnit')}
                    value={item.unit ?? ''}
                    maxLength={32}
                    onChange={(event) => onChange(updateParameter(rules, index, { unit: event.target.value || null }))}
                  />
                  <NumberField
                    label={t('flow.parameterMinimum')}
                    value={item.minimum ?? ''}
                    step="any"
                    error={problem === 'range' ? t(problemCopy.range) : undefined}
                    onChange={(event) =>
                      onChange(updateParameter(rules, index, { minimum: numberOf(event.target.value) }))
                    }
                  />
                  <NumberField
                    label={t('flow.parameterMaximum')}
                    value={item.maximum ?? ''}
                    step="any"
                    onChange={(event) =>
                      onChange(updateParameter(rules, index, { maximum: numberOf(event.target.value) }))
                    }
                  />
                  <CheckboxField
                    label={t('flow.parameterAssignable')}
                    checked={item.assignable}
                    onChange={(event) => onChange(updateParameter(rules, index, { assignable: event.target.checked }))}
                  />
                </div>
              </li>
            );
          })}
        </ul>
      )}
      <div className="authoring-add">
        <form
          className="authoring-flow-search"
          onSubmit={(event) => {
            event.preventDefault();
            const next = name.trim();
            if (!next) return;
            onChange(addParameter(rules, next));
            setName('');
          }}
        >
          <TextField
            label={t('flow.parameterName')}
            value={name}
            maxLength={64}
            onChange={(event) => setName(event.target.value)}
          />
          <ActionButton type="submit" label={t('flow.addParameter')} disabled={!name.trim()} />
        </form>
      </div>
    </Section>
  );
}

export const hasParameterProblems = (rules: RulesModel, takenNames: string[]) =>
  parameterProblems(rules, takenNames).size > 0;
