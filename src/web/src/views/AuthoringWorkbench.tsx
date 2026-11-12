import { useEffect, useMemo, useState } from 'react';
import { ActionButton } from '../components/ActionButton';
import { PanelHeader } from '../components/common';
import { SelectField, TextField } from '../components/FormFields';
import { Notice } from '../components/Notice';
import { PanelLoading } from '../components/LoadingIndicator';
import { discoverConnection, listPoints, loadConnections, planSetPoint, resolveInput } from './connections';
import type { CatalogPoint, Connection, DiscoveredObject, ResolvePreview, SetPointPlan } from './connections';
import './connections.css';
import './authoring.css';

type Template = { id: string; label: string; inputs: string[]; setPoint?: { name: string; delta: number } };

// Starter logic modules: the author picks one, the platform decides per asset where each input comes from.
const templates: Template[] = [
  { id: 'location', label: 'Location (latitude and longitude)', inputs: ['Latitude', 'Longitude'] },
  { id: 'casing', label: 'Casing pressure', inputs: ['CasingPressure'] },
  { id: 'snapshot', label: 'Surveillance snapshot', inputs: ['CasingPressure', 'TubingPressure', 'PumpFillage'] },
  { id: 'custom', label: 'Custom (pick points from the asset)', inputs: [] },
  { id: 'bump', label: 'Pressures and a set point change', inputs: ['CasingPressure', 'TubingPressure'], setPoint: { name: 'IdealPF', delta: 1 } },
];

const outcomeText: Record<string, string> = {
  Compatible: 'Runs', NotApplicable: 'Skipped', Misconfigured: 'Needs fixing', NeedsSelection: 'Pick a source', CannotDetermine: 'Unknown',
};

type Row = { input: string; busy: boolean; basis: string; preview: ResolvePreview | null; error: string | null };

function format(preview: ResolvePreview): string {
  const value = preview.value;
  if (!value || value.value === null) return preview.readProblem ?? '';
  return `${Number(value.value.toFixed(5))} ${value.unit ?? ''}`.trim();
}

/** POC: choose a connection, an asset and a template, then see how every input resolves for that asset before anything runs. */
export function AuthoringWorkbench() {
  const [connections, setConnections] = useState<Connection[] | null>(null);
  const [connectionId, setConnectionId] = useState('');
  const [assets, setAssets] = useState<DiscoveredObject[]>([]);
  const [assetId, setAssetId] = useState('');
  const [filter, setFilter] = useState('');
  const [templateId, setTemplateId] = useState(templates[0].id);
  const [rows, setRows] = useState<Row[]>([]);
  const [plan, setPlan] = useState<SetPointPlan | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [loadingAssets, setLoadingAssets] = useState(false);
  const [planning, setPlanning] = useState(false);
  const [points, setPoints] = useState<CatalogPoint[] | null>(null);
  const [pointFilter, setPointFilter] = useState('');
  const [pointGroup, setPointGroup] = useState('');
  const [extra, setExtra] = useState<string[]>([]);
  const [loadingPoints, setLoadingPoints] = useState(false);

  const template = templates.find(item => item.id === templateId) ?? templates[0];
  const connection = connections?.find(item => item.id === connectionId) ?? null;

  useEffect(() => {
    const controller = new AbortController();
    loadConnections(controller.signal).then(items => {
      const usable = items.filter(item => item.isEnabled);
      setConnections(usable);
      if (usable[0]) setConnectionId(usable[0].id);
    }).catch(() => { if (!controller.signal.aborted) { setConnections([]); setMessage('Connections could not be loaded.'); } });
    return () => controller.abort();
  }, []);

  useEffect(() => {
    setAssets([]); setAssetId(''); setRows([]); setPlan(null);
    if (!connection) return;
    setLoadingAssets(true);
    let live = true;
    discoverConnection(connection).then(found => { if (live) setAssets(found.items); })
      .catch(() => { if (live) setMessage('Assets could not be discovered on this connection.'); })
      .finally(() => { if (live) setLoadingAssets(false); });
    return () => { live = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [connection?.id]);

  useEffect(() => { setRows([]); setPlan(null); setExtra([]); setPoints(null); }, [assetId, templateId]);

  const inputs = useMemo(() => [...template.inputs, ...extra], [template, extra]);
  const shownPoints = useMemo(() => {
    const needle = pointFilter.trim().toLowerCase();
    return (points ?? []).filter(point => (!pointGroup || point.group === pointGroup) && (!needle || point.name.toLowerCase().includes(needle))).slice(0, 400);
  }, [points, pointFilter, pointGroup]);
  const groups = useMemo(() => [...new Set((points ?? []).map(point => point.group))], [points]);

  async function browse() {
    if (!connection || !assetId) return;
    setLoadingPoints(true); setMessage(null);
    try { setPoints(await listPoints(connection.id, assetId)); }
    catch { setMessage('Points could not be listed for this asset. Only ForeSite connections list points today.'); }
    finally { setLoadingPoints(false); }
  }

  const shown = useMemo(() => {
    const needle = filter.trim().toLowerCase();
    return (needle ? assets.filter(item => `${item.displayName} ${item.externalId}`.toLowerCase().includes(needle)) : assets).slice(0, 200);
  }, [assets, filter]);

  async function resolveRow(input: string, basis: string) {
    if (!connection || !assetId) return;
    setRows(current => current.map(row => row.input === input ? { ...row, busy: true, basis, error: null } : row));
    try {
      const preview = await resolveInput(connection.id, assetId, input, basis || undefined);
      setRows(current => current.map(row => row.input === input ? { ...row, busy: false, preview } : row));
    } catch {
      setRows(current => current.map(row => row.input === input ? { ...row, busy: false, error: 'The connection did not answer. Try again.' } : row));
    }
  }

  async function preview() {
    setMessage(null); setPlan(null);
    setRows(inputs.map(input => ({ input, busy: true, basis: '', preview: null, error: null })));
    for (const input of inputs) await resolveRow(input, '');
  }

  async function runPlan() {
    if (!connection || !template.setPoint) return;
    setPlanning(true); setMessage(null);
    try { setPlan(await planSetPoint(connection.id, assetId, template.setPoint.name, template.setPoint.delta)); }
    catch { setMessage('The set point plan could not be made. ForeSite can take up to a minute; try again.'); }
    finally { setPlanning(false); }
  }

  if (!connections) return <PanelLoading label="Loading connections" />;
  const runnable = rows.length > 0 && rows.every(row => row.preview?.result.outcome === 'Compatible');

  return <section className="authoring" aria-label="Authoring workbench">
    <PanelHeader title="Authoring workbench" />
    <Notice announce={false}>Proof of concept. Pick a connection, an asset and a starter module. Each input is resolved against the live system for that asset, so you see what would run, what would be skipped and why. Nothing is written.</Notice>
    {message && <Notice tone="warning">{message}</Notice>}
    <div className="authoring-grid">
      <div className="connection-form">
        <SelectField label="Connection" value={connectionId} onChange={event => setConnectionId(event.target.value)}>
          {connections.map(item => <option key={item.id} value={item.id}>{item.name} ({item.adapterKey})</option>)}
        </SelectField>
        <TextField label="Find an asset" value={filter} onChange={event => setFilter(event.target.value)} />
        <SelectField label={loadingAssets ? 'Asset (loading)' : `Asset (${assets.length})`} value={assetId} onChange={event => setAssetId(event.target.value)}>
          <option value="">Choose an asset</option>
          {shown.map(item => <option key={item.externalId} value={item.externalId}>{item.displayName} [{item.externalId}]</option>)}
        </SelectField>
        <SelectField label="Starter module" value={templateId} onChange={event => setTemplateId(event.target.value)}>
          {templates.map(item => <option key={item.id} value={item.id}>{item.label}</option>)}
        </SelectField>
        <p className="connection-help">Inputs: {inputs.join(', ') || 'none yet. Browse the points below.'}{template.setPoint ? `. Then ${template.setPoint.name} +${template.setPoint.delta}.` : '.'}</p>
        <div className="connection-actions">
          <ActionButton label="Preview on this asset" variant="primary" disabled={!assetId || inputs.length === 0} onClick={() => void preview()} />
          <ActionButton label="Browse points" variant="secondary" busy={loadingPoints} disabled={!assetId} onClick={() => void browse()} />
          {template.setPoint && <ActionButton label="Plan the set point change" variant="secondary" busy={planning} disabled={!assetId} onClick={() => void runPlan()} />}
        </div>
      </div>
      <div>
        {points && <div className="connection-form">
          <SelectField label="Where from" value={pointGroup} onChange={event => setPointGroup(event.target.value)}>
            <option value="">All (surveillance, well test, well)</option>
            {groups.map(group => <option key={group} value={group}>{group}</option>)}
          </SelectField>
          <TextField label={`Points on this asset (${points.length})`} value={pointFilter} onChange={event => setPointFilter(event.target.value)} />
          <ul className="authoring-points">{shownPoints.map(point => <li key={point.name}>
            <span>{point.name} = {point.value} <small>({point.basis}{Number.isFinite(Number(point.value)) ? '' : ', text'})</small></span>
            <ActionButton label="Add" variant="quiet" aria-label={`Add ${point.name}`} disabled={extra.includes(point.name)}
              onClick={() => setExtra(current => [...current, point.name])} />
          </li>)}</ul>
        </div>}
        {rows.length > 0 && <table className="authoring-table">
          <thead><tr><th scope="col">Input</th><th scope="col">Result</th><th scope="col">Source</th><th scope="col">Value</th><th scope="col">Why</th></tr></thead>
          <tbody>{rows.map(row => {
            const result = row.preview?.result;
            const picked = result?.candidates[0];
            return <tr key={row.input}>
              <th scope="row">{row.input}</th>
              <td data-outcome={result?.outcome}>{row.busy ? 'Checking…' : row.error ?? (result ? outcomeText[result.outcome] ?? result.outcome : '')}</td>
              <td>{result?.outcome === 'NeedsSelection'
                ? <SelectField label={`Source for ${row.input}`} value={row.basis} onChange={event => void resolveRow(row.input, event.target.value)}>
                  <option value="">Choose…</option>
                  {result.candidates.map(candidate => <option key={candidate.basis} value={candidate.basis}>{candidate.basis}: {candidate.description}</option>)}
                </SelectField>
                : result?.outcome === 'Compatible' ? picked?.description : ''}</td>
              <td>{row.preview ? format(row.preview) : ''}</td>
              <td>{result?.reasons.join(' ')}</td>
            </tr>;
          })}</tbody>
        </table>}
        {rows.length > 0 && !rows.some(row => row.busy) && (runnable
          ? <Notice tone="success">This module would run on this asset.</Notice>
          : <Notice tone="warning">This module would not run on this asset as it stands. Skipped inputs are explained above.</Notice>)}
        {plan && (plan.allowed
          ? <Notice tone="info">{`Dry run: ${plan.description || plan.setPoint} would change from ${plan.current} to ${plan.proposed} ${plan.unit} (allowed ${plan.rangeLow}–${plan.rangeHigh}). Nothing was sent.`}</Notice>
          : <Notice tone="warning">{`Not allowed: ${plan.refusal ?? 'refused'}`}</Notice>)}
      </div>
    </div>
  </section>;
}
