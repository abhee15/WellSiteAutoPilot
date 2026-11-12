import { Notice } from '../components/Notice';
import type { ModuleOutcome, ModuleRun } from './authoring';
import { showVal } from './authoring';

const outcomeCopy: Record<ModuleOutcome, { tone: 'success' | 'warning' | 'info'; text: string }> = {
  Runs: { tone: 'success', text: 'Runs on this asset. Every required input resolved.' },
  Skipped: { tone: 'info', text: 'Skipped on this asset. A required input does not exist here. That is not a failure.' },
  NeedsSetup: { tone: 'warning', text: 'Needs a choice. An input has more than one possible source; pin a basis on the input.' },
  Unavailable: { tone: 'warning', text: 'Could not be decided. A provider did not answer, so the inputs are unknown. Try again.' },
  Invalid: { tone: 'warning', text: 'The module has errors, so it was not run. Fix the problems listed in the outline.' },
};

/** Test evidence for one run: what each input resolved to, every calculation, and each output with its set point dry run. */
export function AuthoringResults({ run, stale, onSelect }: { run: ModuleRun; stale: boolean; onSelect: (name: string) => void }) {
  const copy = outcomeCopy[run.outcome];
  const pick = (name: string) => <button type="button" className="authoring-link" onClick={() => onSelect(name)}>{name}</button>;
  return <div className="authoring-results" data-stale={stale}>
    {stale && <Notice tone="warning">The module changed after this test. These results are out of date. Run the test again.</Notice>}
    <Notice tone={copy.tone}>{copy.text}</Notice>
    {run.reasons.length > 0 && <ul className="authoring-reasons">{run.reasons.map(reason => <li key={reason}>{reason}</li>)}</ul>}

    <h4>Inputs</h4>
    <table className="authoring-table">
      <thead><tr><th scope="col">Input</th><th scope="col">Result</th><th scope="col">Value</th><th scope="col">From</th></tr></thead>
      <tbody>{run.inputs.map(input => <tr key={input.name}>
        <th scope="row">{pick(input.name)}{input.required ? '' : ' (optional)'}</th>
        <td data-outcome={input.resolution}>{input.resolution}</td>
        <td>{showVal(input.value, input.unit)}</td>
        <td>{input.usedInput ? `${input.usedInput}${input.basis ? ` · ${input.basis}` : ''}` : input.source ?? ''}
          {input.reasons.length > 0 && <small className="authoring-why"> {input.reasons.join(' ')}</small>}
          {input.candidates.length > 0 && <small className="authoring-why"> Options: {input.candidates.map(item => `${item.basis} (${item.source})`).join(', ')}</small>}
        </td>
      </tr>)}</tbody>
    </table>

    {run.steps.length > 0 && <>
      <h4>Calculations</h4>
      <table className="authoring-table">
        <thead><tr><th scope="col">Step</th><th scope="col">Expression</th><th scope="col">Value</th></tr></thead>
        <tbody>{run.steps.map(step => <tr key={step.name}>
          <th scope="row">{pick(step.name)}</th><td><code>{step.expression}</code></td><td>{showVal(step.value)}</td>
        </tr>)}</tbody>
      </table>
    </>}

    <h4>Outputs</h4>
    <table className="authoring-table">
      <thead><tr><th scope="col">Output</th><th scope="col">Value</th><th scope="col">Detail</th></tr></thead>
      <tbody>{run.outputs.map(output => <tr key={output.name}>
        <th scope="row">{pick(output.name)}</th>
        <td>{showVal(output.value, output.kind === 'value' ? output.unit : null)}</td>
        <td>{output.kind === 'setpoint'
          ? output.plan
            ? `${output.plan.allowed ? 'Dry run' : 'Refused'}: ${output.plan.setPoint} ${output.plan.current} → ${output.plan.proposed} ${output.plan.unit} (allowed ${output.plan.rangeLow}–${output.plan.rangeHigh}). ${output.note ?? ''}`
            : output.note ?? 'No change proposed.'
          : ''}</td>
      </tr>)}</tbody>
    </table>
  </div>;
}
