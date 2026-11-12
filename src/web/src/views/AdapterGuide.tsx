import type { Connection } from './connections';

const capabilities: { key: string; label: string; catalog: string; resolve: string; read: string; write: string }[] = [
  { key: 'foresite', label: 'ForeSite', catalog: 'Yes: surveillance, latest well test and well record points', resolve: 'Yes', read: 'Yes, with the unit ForeSite reports', write: 'Dry-run set point plan (IdealPF and other writable set points)' },
  { key: 'cygnet', label: 'CygNet', catalog: 'Not yet', resolve: 'Yes (pump fillage and other mapped inputs)', read: 'Yes', write: 'Not yet' },
];

/** Explains the adapter model in the terms an author needs, with what each of this system's connections supports. */
export function AdapterGuide({ connections }: { connections: Connection[] }) {
  const mine = capabilities.filter(item => connections.some(connection => connection.adapterKey.toLowerCase().includes(item.key)));
  return <div className="authoring-guide">
    <h3>How adapters and logic modules fit together</h3>
    <p>A logic module says what it needs and what to do with it. It never says which system supplies the data. An adapter
      connects the platform to one kind of system, and the platform asks it five questions:</p>
    <ol>
      <li><strong>Catalog.</strong> What does this asset offer? Used by the point browser.</li>
      <li><strong>Resolve.</strong> Can this input be supplied for this asset, from which source, on which basis (live, daily average, well test…)? The answer is Runs, Skipped, Needs a choice, or Could not decide.</li>
      <li><strong>Read.</strong> Get the value, with its unit, the time it was observed and its quality. Old or bad readings are refused, not used.</li>
      <li><strong>Write.</strong> Propose a change to a set point. Authoring only ever plans an absolute value, checks it against the module limits and the controller range, and shows the result as a dry run. Nothing is sent.</li>
      <li><strong>Describe.</strong> Say what the adapter supports, so this page and tests can tell you.</li>
    </ol>
    <p>In a module, an <em>input</em> is a name for a quantity (such as CasingPressure) with the unit, basis and oldest age it accepts; which point answers is a <em>binding</em> on each system. A <em>parameter</em> is a value you can tune. A <em>calculation</em> is a
      named result built from those. An <em>output</em> publishes a value or proposes a set point change. A missing value stays
      missing all the way to the output; it is never turned into zero.</p>
    <p>Because the same module can run on assets served by different adapters, test it on more than one asset: a module that runs on a
      well with surveillance data may be skipped on a well that only has well tests.</p>
    <table className="authoring-table">
      <thead><tr><th scope="col">Adapter</th><th scope="col">Catalog</th><th scope="col">Resolve</th><th scope="col">Read</th><th scope="col">Write</th></tr></thead>
      <tbody>{(mine.length ? mine : capabilities).map(item => <tr key={item.key}>
        <th scope="row">{item.label}</th><td>{item.catalog}</td><td>{item.resolve}</td><td>{item.read}</td><td>{item.write}</td>
      </tr>)}</tbody>
    </table>
  </div>;
}
