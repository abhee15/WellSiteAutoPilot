import { ActionButton } from '../components/ActionButton';
import { CheckboxField, SelectField, TextAreaField, TextField } from '../components/FormFields';
import type { CatalogPoint } from './connections';
import type { Analysis, BindingSource, ModuleDraft, ModuleRun, OutputLimits } from './authoring';
import { showAge, showVal } from './authoring';
import { kindLabel, nodeKindOf, type NodeKind } from './authoringDraft';

const bases = ['', 'Static', 'Current', 'DailyAverage', 'TestSample', 'History'];
const quantities = ['Latitude', 'Longitude', 'CasingPressure', 'TubingPressure', 'PumpSpm', 'PumpFillage'];

type Props = {
  draft: ModuleDraft; kind: NodeKind; name: string; analysis: Analysis | null; run: ModuleRun | null;
  points: CatalogPoint[] | null; sources: BindingSource[]; onSources: (sources: BindingSource[]) => void;
  onChange: (patch: Record<string, unknown>) => void; onRemove: () => void; onSelect: (name: string) => void;
};

const number = (text: string): number | null => text.trim() === '' || Number.isNaN(Number(text)) ? null : Number(text);

/** Edits one node and shows where it sits: what it reads, what reads it, its last value and its problems. */
export function AuthoringNodeEditor({ draft, kind, name, analysis, run, points, sources, onSources, onChange, onRemove, onSelect }: Props) {
  const node = { input: draft.inputs, parameter: draft.parameters, step: draft.steps, output: draft.outputs }[kind].find(item => item.name === name);
  if (!node) return null;
  const graph = analysis?.graph.find(item => item.name === name);
  const problems = (analysis?.problems ?? []).filter(problem => problem.node === name);
  const traced = run?.steps.find(item => item.name === name) ?? run?.parameters.find(item => item.name === name);
  const input = kind === 'input' ? draft.inputs.find(item => item.name === name) : undefined;
  const output = kind === 'output' ? draft.outputs.find(item => item.name === name) : undefined;
  const inputTrace = run?.inputs.find(item => item.name === name);
  const outputResult = run?.outputs.find(item => item.name === name);
  const limit = (key: keyof OutputLimits, text: string) => onChange({ limits: { ...output?.limits, [key]: number(text) } });
  const fieldProblem = (needle: RegExp) => problems.find(problem => needle.test(problem.message))?.message;

  const link = (target: string) => <button key={target} type="button" className="authoring-link" onClick={() => onSelect(target)}>
    {target} <small>({nodeKindOf(draft, target) ? kindLabel[nodeKindOf(draft, target)!].toLowerCase() : '?'})</small></button>;

  return <div className="connection-form authoring-editor" aria-label={`${kindLabel[kind]} ${name}`}>
    <h3>{kindLabel[kind]}: {name}</h3>
    <TextField label="Name" value={name} onChange={event => onChange({ name: event.target.value })}
      helper="Letters, digits and underscores. Renaming updates the calculations that use it." />
    {problems.map(problem => <p key={problem.message} className="authoring-problem" data-severity={problem.severity}>{problem.severity}: {problem.message}</p>)}

    {input && <>
      <p className="connection-help">An input says what the module needs, not where it comes from. The module is portable; the points that answer
        are chosen under Bindings for this system.</p>
      <TextField label="Quantity" value={input.quantity} list="authoring-quantities" error={fieldProblem(/measures|provider point/)}
        onChange={event => onChange({ quantity: event.target.value })} helper={`Mapped today: ${quantities.join(', ')}.`} />
      <datalist id="authoring-quantities">{quantities.map(item => <option key={item} value={item} />)}</datalist>
      <TextField label="Unit the calculations expect" value={input.unit ?? ''} onChange={event => onChange({ unit: event.target.value || null })}
        helper="Readings are converted to this unit. A reading that cannot be converted is refused, never guessed." />
      <SelectField label="Basis" value={input.basis ?? ''} onChange={event => onChange({ basis: event.target.value || null })}>
        {bases.map(basis => <option key={basis} value={basis}>{basis || 'Any'}</option>)}
      </SelectField>
      <TextField label="Oldest reading to accept (seconds)" value={input.maxAgeSeconds?.toString() ?? ''}
        onChange={event => onChange({ maxAgeSeconds: number(event.target.value) })} error={fieldProblem(/maximum age/)}
        helper="Empty accepts any age. For decisions that change a set point, set a limit." />
      <CheckboxField label="Required. The module is skipped on assets that cannot supply it." checked={input.required}
        onChange={event => onChange({ required: event.target.checked })} />
      <CheckboxField label="Accept readings of uncertain quality" checked={input.allowUncertain === true}
        onChange={event => onChange({ allowUncertain: event.target.checked })} />

      <h4>Bindings on this system</h4>
      <p className="connection-help">Optional. Sources are tried in order; the first that answers is used. With none, the platform picks by quantity and basis.</p>
      {sources.map((source, index) => <div key={`${index}-${source.input}`} className="authoring-source">
        <TextField label={`Source ${index + 1}`} value={source.input}
          onChange={event => onSources(sources.map((item, at) => at === index ? { ...item, input: event.target.value } : item))} />
        <SelectField label="Basis" value={source.basis ?? ''}
          onChange={event => onSources(sources.map((item, at) => at === index ? { ...item, basis: event.target.value || null } : item))}>
          {bases.map(basis => <option key={basis} value={basis}>{basis || 'Any'}</option>)}
        </SelectField>
        <ActionButton label="Remove" variant="quiet" aria-label={`Remove source ${index + 1}`} onClick={() => onSources(sources.filter((_, at) => at !== index))} />
      </div>)}
      <div className="connection-actions">
        <ActionButton label="Add source" variant="secondary" onClick={() => onSources([...sources, { input: '', basis: null }])} />
      </div>
      {points && <details className="authoring-points-box">
        <summary>Add a source from this asset's points</summary>
        <ul className="authoring-points">{points.slice(0, 300).map(point => <li key={point.name}>
          <span>{point.name} = {point.value} <small>({point.basis})</small></span>
          <ActionButton label="Use" variant="quiet" aria-label={`Use ${point.name} for ${name}`}
            onClick={() => onSources([...sources, { input: point.name, basis: null }])} />
        </li>)}</ul>
      </details>}
    </>}

    {kind === 'parameter' && <>
      <TextField label="Value" value={(node as { value: string }).value} onChange={event => onChange({ value: event.target.value })}
        helper="A number, true or false, or text. Test runs can try other values without changing the module." />
      <TextField label="Unit" value={(node as { unit?: string | null }).unit ?? ''} onChange={event => onChange({ unit: event.target.value || null })}
        helper="A parameter with a unit is compared and combined as a quantity, so 250 psia is never mixed up with 250 psig." />
    </>}

    {(kind === 'step' || kind === 'output') && <TextAreaField label={output?.kind === 'setpoint' ? 'New set point value (expression)' : 'Expression'}
      value={(node as { expression: string }).expression} rows={3} onChange={event => onChange({ expression: event.target.value })}
      helper="Use input, parameter and calculation names. + - * / % comparisons && || !, and if(), min(), max(), abs(), round(), avg(), clamp(), coalesce(), has()." />}

    {output && <>
      <SelectField label="What it does" value={output.kind} onChange={event => onChange({ kind: event.target.value })}>
        <option value="value">Publish a value</option>
        <option value="setpoint">Propose a set point value</option>
      </SelectField>
      <TextField label="Unit" value={output.unit ?? ''} onChange={event => onChange({ unit: event.target.value || null })}
        helper="The result is converted to this unit. Pressure differences can be published as psi." />
      {output.kind === 'setpoint' && <>
        <TextField label="Set point to change" value={output.target ?? ''} onChange={event => onChange({ target: event.target.value })}
          helper="Binding for this system. The proposal is an absolute value, checked and shown as a dry run. Nothing is sent to the controller." />
        <TextField label="Only when (condition)" value={output.when ?? ''} onChange={event => onChange({ when: event.target.value || null })}
          helper="Optional. When this is false the module holds and proposes nothing." />
        <fieldset className="authoring-limits">
          <legend>Limits the proposal must stay inside</legend>
          <TextField label="Minimum" value={output.limits?.min?.toString() ?? ''} onChange={event => limit('min', event.target.value)} />
          <TextField label="Maximum" value={output.limits?.max?.toString() ?? ''} onChange={event => limit('max', event.target.value)} />
          <TextField label="Largest step from current" value={output.limits?.maxStep?.toString() ?? ''} onChange={event => limit('maxStep', event.target.value)}
            error={fieldProblem(/maximum step/)} />
        </fieldset>
      </>}
    </>}

    <dl className="authoring-trace">
      {graph && graph.dependsOn.length > 0 && <><dt>Reads</dt><dd>{graph.dependsOn.map(link)}</dd></>}
      {graph && graph.usedBy.length > 0 && <><dt>Used by</dt><dd>{graph.usedBy.map(link)}</dd></>}
      {inputTrace && <><dt>Last test</dt><dd>{showVal(inputTrace.value, inputTrace.unit)}{inputTrace.usedInput ? ` from ${inputTrace.usedInput}` : ''}
        {inputTrace.ageSeconds !== undefined && inputTrace.ageSeconds !== null ? `, ${showAge(inputTrace.ageSeconds)}` : ''}</dd></>}
      {traced && <><dt>Last test</dt><dd>{showVal(traced.value)}</dd></>}
      {outputResult && <><dt>Last test</dt><dd>{showVal(outputResult.value, outputResult.unit)}</dd></>}
    </dl>
    <div className="connection-actions"><ActionButton label={`Remove ${kindLabel[kind].toLowerCase()}`} variant="quiet" onClick={onRemove} /></div>
  </div>;
}
