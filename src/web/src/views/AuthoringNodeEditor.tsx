import { ActionButton } from '../components/ActionButton';
import { CheckboxField, SelectField, TextAreaField, TextField } from '../components/FormFields';
import type { CatalogPoint } from './connections';
import type { Analysis, BindingSource, DraftRule, InputHistory, ModuleDraft, ModuleRun, OutputLimits, RowTrace } from './authoring';
import { showAge, showVal } from './authoring';
import { kindLabel, nodeKindOf, type NodeKind } from './authoringDraft';

const bases = ['', 'Static', 'Current', 'DailyAverage', 'TestSample', 'History', 'Saved'];
const reductions = ['last', 'first', 'avg', 'min', 'max'];
const quantities = ['Latitude', 'Longitude', 'CasingPressure', 'TubingPressure', 'PumpSpm', 'PumpFillage'];

type Props = {
  draft: ModuleDraft; kind: NodeKind; name: string; analysis: Analysis | null; run: ModuleRun | null;
  points: CatalogPoint[] | null; sources: BindingSource[]; onSources: (sources: BindingSource[]) => void;
  onChange: (patch: Record<string, unknown>) => void; onRemove: () => void; onSelect: (name: string) => void;
};

const move = <T,>(items: T[], from: number, to: number): T[] => {
  const next = [...items];
  const [item] = next.splice(from, 1);
  next.splice(to, 0, item);
  return next;
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

  const lastValue = (target: string) => {
    const read = run?.inputs.find(item => item.name === target);
    if (read) return showVal(read.value, read.unit ?? undefined);
    const other = run?.parameters.find(item => item.name === target) ?? run?.steps.find(item => item.name === target);
    return other ? showVal(other.value) : null;
  };

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
      <CheckboxField label="Read from history instead of the current value (a day, a time of day, or the last few hours)" checked={!!input.history}
        onChange={event => onChange({ history: event.target.checked ? { daysBack: 0, atLocal: 'now', windowMinutes: 60, reduce: 'last' } : null })} />
      {input.history && <HistoryFields history={input.history} error={fieldProblem(/history|window|time of day|days back/i)} onChange={history => onChange({ history })} />}
      <fieldset className="authoring-limits">
        <legend>Readings this input trusts</legend>
        <TextField label="Smallest valid value" value={input.validMin?.toString() ?? ''} onChange={event => onChange({ validMin: number(event.target.value) })} />
        <TextField label="Largest valid value" value={input.validMax?.toString() ?? ''} onChange={event => onChange({ validMax: number(event.target.value) })}
          helper="A reading outside this range is distrusted and never judged, for example a pump fillage of 140 %." />
      </fieldset>

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
      <fieldset className="authoring-limits">
        <legend>Allowed range</legend>
        <TextField label="Minimum" value={(node as { min?: number | null }).min?.toString() ?? ''} onChange={event => onChange({ min: number(event.target.value) })} />
        <TextField label="Maximum" value={(node as { max?: number | null }).max?.toString() ?? ''} onChange={event => onChange({ max: number(event.target.value) })}
          helper="Optional. Neither the default nor a per-asset override may go outside it." />
      </fieldset>
    </>}

    {(kind === 'step' || kind === 'output') && <TextAreaField label={kind === 'step' && (node as { rules?: DraftRule[] | null }).rules?.length ? 'Otherwise (expression)' : output?.kind === 'setpoint' ? 'New set point value (expression)' : output?.kind === 'recommendation' ? 'Recommend a review when (expression)' : 'Expression'}
      value={(node as { expression: string }).expression} rows={3} onChange={event => onChange({ expression: event.target.value })}
      helper="Use input, parameter and calculation names. + - * / % comparisons && || !, and if(), min(), max(), abs(), round(), avg(), clamp(), coalesce(), has()." />}

    {kind === 'step' && <RuleRows rules={(node as { rules?: DraftRule[] | null }).rules ?? []} trace={traced && 'rows' in traced ? traced.rows ?? undefined : undefined} onChange={rules => onChange({ rules: rules.length > 0 ? rules : null })} />}

    {output && <>
      <SelectField label="What it does" value={output.kind} onChange={event => onChange({ kind: event.target.value })}>
        <option value="value">Publish a value</option>
        <option value="setpoint">Propose a set point value</option>
        <option value="recommendation">Recommend a review</option>
      </SelectField>
      {output.kind === 'recommendation' && <>
        <TextField label="Outcome code" value={output.code ?? ''} onChange={event => onChange({ code: event.target.value || null })}
          error={fieldProblem(/outcome code/)} helper="Capital letters, digits and underscores, for example PUMP_FILLAGE_LOW. Empty uses the output name." />
        <TextField label="Title" value={output.title ?? ''} onChange={event => onChange({ title: event.target.value })} error={fieldProblem(/title/)} />
        <TextAreaField label="Detail" value={output.detail ?? ''} rows={4} onChange={event => onChange({ detail: event.target.value || null })}
          error={fieldProblem(/text uses/)} helper="Write {Name} for a value with its unit, or {Name:0.#} for the number alone. Nothing is sent to a controller." />
      </>}
      {output.kind !== 'recommendation' && <TextField label="Unit" value={output.unit ?? ''} onChange={event => onChange({ unit: event.target.value || null })}
        helper="The result is converted to this unit. Pressure differences can be published as psi." />}
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
      {graph && graph.dependsOn.length > 0 && <><dt>Reads</dt><dd>{graph.dependsOn.map(target => <span key={target} className="authoring-read">{link(target)}{lastValue(target) !== null && <small> = {lastValue(target)}</small>}</span>)}</dd></>}
      {graph && graph.usedBy.length > 0 && <><dt>Used by</dt><dd>{graph.usedBy.map(link)}</dd></>}
      {inputTrace && <><dt>Last test</dt><dd>{showVal(inputTrace.value, inputTrace.unit)}{inputTrace.usedInput ? ` from ${inputTrace.usedInput}` : ''}
        {inputTrace.ageSeconds !== undefined && inputTrace.ageSeconds !== null ? `, ${showAge(inputTrace.ageSeconds)}` : ''}</dd></>}
      {traced && <><dt>Last test</dt><dd>{showVal(traced.value)}</dd></>}
      {outputResult && <><dt>Last test</dt><dd>{showVal(outputResult.value, outputResult.unit)}</dd></>}
    </dl>
    <div className="connection-actions"><ActionButton label={`Remove ${kindLabel[kind].toLowerCase()}`} variant="quiet" onClick={onRemove} /></div>
  </div>;
}

function HistoryFields({ history, error, onChange }: { history: InputHistory; error?: string; onChange: (history: InputHistory) => void }) {
  const set = (patch: Partial<InputHistory>) => onChange({ ...history, ...patch });
  return <fieldset className="authoring-limits">
    <legend>History window</legend>
    <TextField label="Ends at (HH:mm site time, a parameter name, or now)" value={history.atLocal} error={error}
      onChange={event => set({ atLocal: event.target.value })}
      helper="Use now for the last few hours. A time of day or a parameter such as GaugeOff is read in the site's time zone, never the server's." />
    <TextField label="Days back" value={String(history.daysBack)} onChange={event => set({ daysBack: number(event.target.value) ?? 0 })}
      helper="0 is today, 1 is yesterday. Not used when the window ends now." />
    <TextField label="Window (minutes)" value={String(history.windowMinutes ?? 10)} onChange={event => set({ windowMinutes: number(event.target.value) ?? 10 })}
      helper="Longer than the sampling interval, or the window can come back empty. An empty or cut-off window stops the module rather than counting as zero." />
    <SelectField label="Reduce the window to one value by" value={history.reduce ?? 'last'} onChange={event => set({ reduce: event.target.value })}>
      {reductions.map(item => <option key={item} value={item}>{item}</option>)}
    </SelectField>
  </fieldset>;
}

/** A rule table: rows are tried top to bottom and the first true row decides; the expression is what applies when none match. */
function RuleRows({ rules, trace, onChange }: { rules: DraftRule[]; trace?: RowTrace[]; onChange: (rules: DraftRule[]) => void }) {
  const set = (index: number, patch: Partial<DraftRule>) => onChange(rules.map((rule, at) => at === index ? { ...rule, ...patch } : rule));
  return <fieldset className="authoring-limits authoring-rules">
    <legend>Rules (first true row decides)</legend>
    {rules.map((rule, index) => <div key={index} className="authoring-rule">
      {trace?.[index] && trace[index].when === rule.when && <p className="authoring-rowstate" data-state={trace[index].state}>
        <strong>{trace[index].decided ? 'Decided this run' : trace[index].state === 'Pass' ? 'Passes, but an earlier row already decided' : trace[index].state === 'Fail' ? 'Does not pass' : 'Cannot tell'}</strong>
        {trace[index].reason ? `: ${trace[index].reason}` : ''}</p>}
      <TextField label={`Row ${index + 1}: when`} value={rule.when} onChange={event => set(index, { when: event.target.value })} />
      <TextField label={`Row ${index + 1}: then`} value={rule.then} onChange={event => set(index, { then: event.target.value })} />
      <TextField label={`Row ${index + 1}: why`} value={rule.reason ?? ''} onChange={event => set(index, { reason: event.target.value || null })}
        helper="Shown with the result so a person can see why." />
      <div className="connection-actions">
        <ActionButton label="Up" variant="quiet" aria-label={`Move row ${index + 1} up`} disabled={index === 0} onClick={() => onChange(move(rules, index, index - 1))} />
        <ActionButton label="Down" variant="quiet" aria-label={`Move row ${index + 1} down`} disabled={index === rules.length - 1} onClick={() => onChange(move(rules, index, index + 1))} />
        <ActionButton label="Remove" variant="quiet" aria-label={`Remove row ${index + 1}`} onClick={() => onChange(rules.filter((_, at) => at !== index))} />
      </div>
    </div>)}
    <div className="connection-actions"><ActionButton label="Add rule" variant="secondary" onClick={() => onChange([...rules, { when: '', then: '', reason: null }])} /></div>
  </fieldset>;
}