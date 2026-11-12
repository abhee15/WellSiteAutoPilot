import { ActionButton } from '../components/ActionButton';
import { CheckboxField, SelectField, TextAreaField, TextField } from '../components/FormFields';
import type { CatalogPoint } from './connections';
import type { Analysis, ModuleDraft, ModuleRun } from './authoring';
import { showVal } from './authoring';
import { kindLabel, nodeKindOf, type NodeKind } from './authoringDraft';

const bases = ['', 'Static', 'Current', 'DailyAverage', 'TestSample', 'History'];
const mappedInputs = 'Latitude, Longitude, CasingPressure, TubingPressure, PumpFillage';

type Props = {
  draft: ModuleDraft; kind: NodeKind; name: string; analysis: Analysis | null; run: ModuleRun | null;
  points: CatalogPoint[] | null; onChange: (patch: Record<string, unknown>) => void; onRemove: () => void; onSelect: (name: string) => void;
};

/** Edits one node and shows where it sits: what it reads, what reads it, its current value and its problems. */
export function AuthoringNodeEditor({ draft, kind, name, analysis, run, points, onChange, onRemove, onSelect }: Props) {
  const node = { input: draft.inputs, parameter: draft.parameters, step: draft.steps, output: draft.outputs }[kind].find(item => item.name === name);
  if (!node) return null;
  const graph = analysis?.graph.find(item => item.name === name);
  const problems = (analysis?.problems ?? []).filter(problem => problem.node === name);
  const traced = run?.steps.find(item => item.name === name) ?? run?.parameters.find(item => item.name === name);
  const input = kind === 'input' ? draft.inputs.find(item => item.name === name) : undefined;
  const output = kind === 'output' ? draft.outputs.find(item => item.name === name) : undefined;
  const inputTrace = run?.inputs.find(item => item.name === name);
  const outputResult = run?.outputs.find(item => item.name === name);

  const link = (target: string) => <button key={target} type="button" className="authoring-link" onClick={() => onSelect(target)}>
    {target} <small>({nodeKindOf(draft, target) ? kindLabel[nodeKindOf(draft, target)!].toLowerCase() : '?'})</small></button>;

  return <div className="connection-form authoring-editor" aria-label={`${kindLabel[kind]} ${name}`}>
    <h3>{kindLabel[kind]}: {name}</h3>
    <TextField label="Name" value={name} onChange={event => onChange({ name: event.target.value })}
      helper="Letters, digits and underscores. Renaming updates the calculations that use it." />
    {problems.map(problem => <p key={problem.message} className="authoring-problem" data-severity={problem.severity}>{problem.severity}: {problem.message}</p>)}

    {input && <>
      <CheckboxField label="Required. The module is skipped on assets that cannot supply it." checked={input.required}
        onChange={event => onChange({ required: event.target.checked })} />
      <p className="connection-help">Sources are tried in order. The first one that answers is used, so put the preferred source first.
        Mapped names: {mappedInputs}. Or a point: surveillance:&lt;point&gt;, welltest:&lt;point&gt;, well:&lt;point&gt;.</p>
      {input.sources.map((source, index) => <div key={index} className="authoring-source">
        <TextField label={`Source ${index + 1}`} value={source.input}
          onChange={event => onChange({ sources: input.sources.map((item, at) => at === index ? { ...item, input: event.target.value } : item) })} />
        <SelectField label="Basis" value={source.basis ?? ''}
          onChange={event => onChange({ sources: input.sources.map((item, at) => at === index ? { ...item, basis: event.target.value || null } : item) })}>
          {bases.map(basis => <option key={basis} value={basis}>{basis || 'Any'}</option>)}
        </SelectField>
        <ActionButton label="Remove" variant="quiet" aria-label={`Remove source ${index + 1}`}
          onClick={() => onChange({ sources: input.sources.filter((_, at) => at !== index) })} />
      </div>)}
      <div className="connection-actions">
        <ActionButton label="Add source" variant="secondary" onClick={() => onChange({ sources: [...input.sources, { input: '', basis: null }] })} />
      </div>
      {points && <details className="authoring-points-box">
        <summary>Add a source from this asset's points</summary>
        <ul className="authoring-points">{points.slice(0, 300).map(point => <li key={point.name}>
          <span>{point.name} = {point.value} <small>({point.basis})</small></span>
          <ActionButton label="Use" variant="quiet" aria-label={`Use ${point.name} for ${name}`}
            onClick={() => onChange({ sources: [...input.sources, { input: point.name, basis: null }] })} />
        </li>)}</ul>
      </details>}
    </>}

    {kind === 'parameter' && <TextField label="Value" value={(node as { value: string }).value} onChange={event => onChange({ value: event.target.value })}
      helper="A number, true or false, or text. Test runs can try other values without changing the module." />}

    {(kind === 'step' || kind === 'output') && <TextAreaField label={output?.kind === 'setpoint' ? 'Change to apply (expression)' : 'Expression'}
      value={(node as { expression: string }).expression} rows={3} onChange={event => onChange({ expression: event.target.value })}
      helper="Use input, parameter and calculation names. + - * / % comparisons && || !, and if(), min(), max(), abs(), round(), avg(), clamp(), coalesce(), has()." />}

    {output && <>
      <SelectField label="What it does" value={output.kind} onChange={event => onChange({ kind: event.target.value })}>
        <option value="value">Publish a value</option>
        <option value="setpoint">Propose a set point change</option>
      </SelectField>
      {output.kind === 'setpoint'
        ? <TextField label="Set point to change" value={output.target ?? ''} onChange={event => onChange({ target: event.target.value })}
          helper="Tests show the before and after values and the allowed range. Nothing is sent to the controller." />
        : <TextField label="Unit" value={output.unit ?? ''} onChange={event => onChange({ unit: event.target.value })} />}
    </>}

    <dl className="authoring-trace">
      {graph && graph.dependsOn.length > 0 && <><dt>Reads</dt><dd>{graph.dependsOn.map(link)}</dd></>}
      {graph && graph.usedBy.length > 0 && <><dt>Used by</dt><dd>{graph.usedBy.map(link)}</dd></>}
      {inputTrace && <><dt>Last test</dt><dd>{showVal(inputTrace.value, inputTrace.unit)}{inputTrace.usedInput ? ` from ${inputTrace.usedInput}` : ''}</dd></>}
      {traced && <><dt>Last test</dt><dd>{showVal(traced.value)}</dd></>}
      {outputResult && <><dt>Last test</dt><dd>{showVal(outputResult.value, outputResult.unit)}</dd></>}
    </dl>
    <div className="connection-actions"><ActionButton label={`Remove ${kindLabel[kind].toLowerCase()}`} variant="quiet" onClick={onRemove} /></div>
  </div>;
}
