import { Notice } from '../components/Notice';
import type { ModuleOutcome, ModuleRun, OutputResult } from './authoring';
import { showAge, showVal } from './authoring';

const outcomeCopy: Record<ModuleOutcome, { tone: 'success' | 'warning' | 'info'; text: string }> = {
  Runs: { tone: 'success', text: 'Runs on this asset. Every required input resolved.' },
  Skipped: { tone: 'info', text: 'Skipped on this asset. A required input does not exist here. That is not a failure.' },
  NeedsSetup: { tone: 'warning', text: 'Needs setup. An input has more than one possible source, or its unit or binding does not fit; fix it under the input.' },
  Unavailable: { tone: 'warning', text: 'Could not be decided. A provider did not answer, or a reading was too old or of bad quality, so no result is given.' },
  Invalid: { tone: 'warning', text: 'The module has errors, so it was not run. Fix the problems listed in the outline.' },
};

const statusLabel = { Pass: 'Pass', Fail: 'Fail', Hold: 'Hold', Info: 'Note' } as const;

function SetPointPanel({ output }: { output: OutputResult }) {
  const plan = output.plan;
  const verdict = output.proposed ? (plan?.allowed ? 'Proposed, within limits' : 'Refused') : 'No change proposed';
  return <section className="authoring-safety" aria-label={`Set point proposal ${output.name}`} data-verdict={output.proposed ? (plan?.allowed ? 'ok' : 'refused') : 'none'}>
    <p><strong className="authoring-badge">DRY RUN</strong> {verdict}. Nothing was sent to the controller.</p>
    {plan && <p className="authoring-change">{plan.setPoint}: {plan.current} → <strong>{plan.proposed}</strong> {plan.unit}
      <small> (controller range {plan.rangeLow}–{plan.rangeHigh})</small></p>}
    {output.checks && output.checks.length > 0 && <ul className="authoring-checks">{output.checks.map(check => <li key={check.name} data-status={check.status}>
      <strong>{statusLabel[check.status]}</strong> {check.name}: {check.detail}</li>)}</ul>}
    {output.note && <p className="connection-help">{output.note}</p>}
  </section>;
}

/** Test evidence for one run: which asset and when, what each input resolved to and how fresh it was, every calculation, and each output. */
export function AuthoringResults({ run, stale, onSelect }: { run: ModuleRun; stale: boolean; onSelect: (name: string) => void }) {
  const copy = outcomeCopy[run.outcome];
  const pick = (name: string) => <button type="button" className="authoring-link" onClick={() => onSelect(name)}>{name}</button>;
  const when = run.evaluatedAtUtc ? new Date(run.evaluatedAtUtc).toLocaleString() : null;
  return <div className="authoring-results" data-stale={stale} aria-label="Test evidence">
    <p className="authoring-evidence">{run.mode === 'live' ? `Live read of ${run.externalId ?? 'the asset'}` : 'Typed sample values'}{when ? ` · ${when}` : ''}</p>
    {stale && <Notice tone="warning">The module, bindings or test target changed after this test. These results are out of date. Run the test again.</Notice>}
    <Notice tone={copy.tone}>{copy.text}</Notice>
    {run.reasons.length > 0 && <ul className="authoring-reasons">{run.reasons.map(reason => <li key={reason}>{reason}</li>)}</ul>}

    <h4>Inputs</h4>
    <table className="authoring-table">
      <thead><tr><th scope="col">Input</th><th scope="col">Result</th><th scope="col">Value</th><th scope="col">Evidence</th></tr></thead>
      <tbody>{run.inputs.map(input => <tr key={input.name}>
        <th scope="row">{pick(input.name)}{input.required ? '' : ' (optional)'}</th>
        <td data-outcome={input.resolution}>{input.resolution}</td>
        <td>{showVal(input.value, input.unit)}</td>
        <td>{input.usedInput ? `${input.usedInput}${input.basis ? ` · ${input.basis}` : ''}` : input.source ?? ''}
          {input.ageSeconds !== undefined && input.ageSeconds !== null && <small className="authoring-why">
            {showAge(input.ageSeconds)}{input.maxAgeSeconds ? ` (limit ${input.maxAgeSeconds} s)` : ''}
            {input.timestampKind ? ` · time from ${input.timestampKind}` : ''}{input.value.quality && input.value.quality !== 'Good' ? ` · ${input.value.quality} quality` : ''}</small>}
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
          <th scope="row">{pick(step.name)}</th><td><code>{step.expression}</code></td><td>{showVal(step.value)}{step.matched && <small className="authoring-why"> {step.matched}</small>}</td>
        </tr>)}</tbody>
      </table>
    </>}

    <h4>Outputs</h4>
    <table className="authoring-table">
      <thead><tr><th scope="col">Output</th><th scope="col">Value</th><th scope="col">Detail</th></tr></thead>
      <tbody>{run.outputs.map(output => <tr key={output.name}>
        <th scope="row">{pick(output.name)}</th>
        <td>{showVal(output.value, output.unit)}</td>
        <td>{output.kind === 'setpoint' ? <SetPointPanel output={output} /> : output.advice ? <div className="authoring-advice"><strong>{output.advice.code}</strong>: {output.advice.title}<p>{output.advice.detail}</p></div> : output.kind === 'recommendation' ? (output.note ?? '') : ''}</td>
      </tr>)}</tbody>
    </table>
  </div>;
}
