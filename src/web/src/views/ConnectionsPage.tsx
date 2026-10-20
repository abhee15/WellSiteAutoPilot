import './activity.css';
import type React from 'react';
import { useCallback, useEffect, useState } from 'react';
interface AdapterResponse { key: string; displayName: string; defaultEndpoint: string; supportsDiscovery: boolean; description: string; settingKeys?: string[] | null }
interface ConnectionTest {
  testedRevision: number; testedAtUtc: string; health: string; healthReason?: string | null;
  compatibility: string; compatibilityReason?: string | null; supportsDiscovery: boolean;
}
interface ConnectionResponse {
  id: string; name: string; adapterKey: string; endpoint: string; enabled: boolean; revision: number;
  updatedAtUtc: string; lastTest?: ConnectionTest | null; settings?: Record<string, string> | null;
}
interface DiscoveredAsset {
  externalId: string; displayName: string; providerType: string; parentExternalId?: string | null;
  metadata: Record<string, string>; alreadyImported: boolean;
}
interface DiscoveryResponse { connectionId: string; discoveredAtUtc: string; assets: DiscoveredAsset[] }
interface ApplyDiscoveryResponse { created: number; alreadyPresent: number; assetTypeId: string }

// POC: strings are inline English. Localization keys come with the production build.
const base = '/api/v1/connections';

class ApiProblem extends Error {}

async function call<T>(method: string, path: string, body?: unknown): Promise<T> {
  const response = await fetch(base + path, {
    method,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (response.status === 204) return undefined as T;
  const text = await response.text();
  let parsed: unknown = null;
  try { parsed = text ? JSON.parse(text) : null; } catch { /* not JSON */ }
  if (!response.ok) {
    const problem = parsed as { detail?: string; title?: string; code?: string } | null;
    throw new ApiProblem(problem?.detail ?? problem?.title ?? `Request failed (${response.status}).`);
  }
  return parsed as T;
}

function healthClass(value: string): string {
  return value === 'Healthy' ? 'status-ok' : value === 'Degraded' ? 'status-warn' : 'status-bad';
}

interface Draft { id: string | null; name: string; adapterKey: string; endpoint: string; enabled: boolean; revision: number | null; settings: Record<string, string> }

/** Administration > Connections: adapters, saved connections, test evidence, discovery preview and import. */
export function ConnectionsPage() {
  const [adapters, setAdapters] = useState<AdapterResponse[]>([]);
  const [connections, setConnections] = useState<ConnectionResponse[]>([]);
  const [draft, setDraft] = useState<Draft | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [message, setMessage] = useState<{ kind: 'ok' | 'error'; text: string } | null>(null);
  const [discovery, setDiscovery] = useState<DiscoveryResponse | null>(null);
  const [applied, setApplied] = useState<ApplyDiscoveryResponse | null>(null);

  const refresh = useCallback(async () => {
    try {
      const [a, c] = await Promise.all([
        call<AdapterResponse[]>('GET', '/adapters'),
        call<ConnectionResponse[]>('GET', ''),
      ]);
      setAdapters(a); setConnections(c);
    } catch (caught) {
      setMessage({ kind: 'error', text: caught instanceof Error ? caught.message : 'Could not load connections.' });
    }
  }, []);

  useEffect(() => { void refresh(); }, [refresh]);

  const run = async (label: string, action: () => Promise<void>) => {
    setBusy(label); setMessage(null);
    try { await action(); } catch (caught) {
      setMessage({ kind: 'error', text: caught instanceof Error ? caught.message : 'The operation failed.' });
    } finally { setBusy(null); }
  };

  const adapterName = (key: string) => adapters.find(a => a.key === key)?.displayName ?? key;
  const supportsDiscovery = (key: string) => adapters.find(a => a.key === key)?.supportsDiscovery ?? false;

  const startNew = () => {
    const first = adapters[0];
    setDraft({ id: null, name: '', adapterKey: first?.key ?? '', endpoint: first?.defaultEndpoint ?? '', enabled: true, revision: null, settings: {} });
  };

  const save = () => draft && run('save', async () => {
    const body = { name: draft.name, adapterKey: draft.adapterKey, endpoint: draft.endpoint, enabled: draft.enabled, revision: draft.revision, settings: draft.settings };
    if (draft.id) await call('PUT', `/${draft.id}`, body); else await call('POST', '', body);
    setDraft(null); await refresh();
    setMessage({ kind: 'ok', text: 'Connection saved. Test it to record evidence for this revision.' });
  });

  const test = (c: ConnectionResponse) => run(`test-${c.id}`, async () => {
    await call('POST', `/${c.id}/test`); await refresh();
  });

  const remove = (c: ConnectionResponse) => run(`delete-${c.id}`, async () => {
    await call('DELETE', `/${c.id}`);
    if (discovery?.connectionId === c.id) setDiscovery(null);
    await refresh();
  });

  const discover = (c: ConnectionResponse) => run(`discover-${c.id}`, async () => {
    setApplied(null);
    setDiscovery(await call<DiscoveryResponse>('POST', `/${c.id}/discover`));
  });

  const [templates, setTemplates] = useState<HierarchyTemplate[]>([]);
  const [templateKey, setTemplateKey] = useState('asset-type-device-well');
  const [customLevels, setCustomLevels] = useState('asset,wellType,deviceType,well');
  useEffect(() => { void call<HierarchyTemplate[]>('GET', '/templates').then(setTemplates).catch(() => undefined); }, []);

  const levels = templateKey === 'custom'
    ? customLevels.split(',').map(s => s.trim()).filter(Boolean)
    : templates.find(x => x.key === templateKey)?.levels ?? [];

  const apply = () => discovery && run('apply', async () => {
    const body = templateKey === 'custom' ? { levels } : { templateKey };
    const result = await call<ApplyDiscoveryResponse>('POST', `/${discovery.connectionId}/discover/apply`, body);
    setApplied(result);
    setDiscovery(await call<DiscoveryResponse>('POST', `/${discovery.connectionId}/discover`));
  });

  const pending = discovery?.assets.filter(a => !a.alreadyImported).length ?? 0;
  const tree = buildTree(discovery?.assets ?? [], levels);
  return (
    <section aria-labelledby="connections-heading">
      <h2 id="connections-heading" className="access-intro">Connections</h2>
      <p className="access-note access-intro">
        A connection binds an adapter to one external system. Test records health and compatibility for the saved
        revision; discovery proposes assets, and nothing is created until you apply it.
      </p>
      {message && <p role={message.kind === 'error' ? 'alert' : 'status'}
        className={(message.kind === 'error' ? 'execution-detail-error ' : '') + 'access-intro'}>{message.text}</p>}

      <h3 className="access-intro">Adapters</h3>
      <div className="table-wrap">
        <table aria-label="Adapters">
          <thead><tr><th scope="col">Adapter</th><th scope="col">Discovery</th><th scope="col">Description</th></tr></thead>
          <tbody>{adapters.map(a => (
            <tr key={a.key}><td>{a.displayName}<br /><small>{a.key}</small></td>
              <td>{a.supportsDiscovery ? 'Yes' : 'No'}</td><td>{a.description}</td></tr>
          ))}</tbody>
        </table>
      </div>

      <div className="activity-more">
        <button type="button" className="secondary-button" onClick={startNew} disabled={busy !== null || adapters.length === 0}>
          New connection
        </button>
      </div>

      {draft && (
        <form className="activity-filters" onSubmit={event => { event.preventDefault(); void save(); }}>
          <label>Name
            <input value={draft.name} maxLength={128} required onChange={e => setDraft({ ...draft, name: e.target.value })} />
          </label>
          <label>Adapter
            <select value={draft.adapterKey} disabled={draft.id !== null}
              onChange={e => setDraft({ ...draft, adapterKey: e.target.value,
                endpoint: adapters.find(a => a.key === e.target.value)?.defaultEndpoint ?? draft.endpoint, settings: {} })}>
              {adapters.map(a => <option key={a.key} value={a.key}>{a.displayName}</option>)}
            </select>
          </label>
          <label>Endpoint
            <input type="url" value={draft.endpoint} required onChange={e => setDraft({ ...draft, endpoint: e.target.value })} />
          </label>
          {(adapters.find(a => a.key === draft.adapterKey)?.settingKeys ?? []).map(key => (
            <label key={key}>{key}
              <input value={draft.settings[key] ?? ''}
                onChange={e => setDraft({ ...draft, settings: { ...draft.settings, [key]: e.target.value } })} />
            </label>
          ))}
          <label>
            <span><input type="checkbox" checked={draft.enabled}
              onChange={e => setDraft({ ...draft, enabled: e.target.checked })} /> Enabled</span>
          </label>
          <div className="activity-filter-actions">
            <button type="submit" className="secondary-button" disabled={busy !== null}>Save</button>
            <button type="button" className="secondary-button" onClick={() => setDraft(null)}>Cancel</button>
          </div>
        </form>
      )}

      <h3 className="access-intro">Saved connections</h3>
      {connections.length === 0
        ? <p className="access-note access-intro">No connections yet. Create one to test an adapter.</p>
        : (
          <div className="table-wrap">
            <table aria-label="Connections">
              <thead><tr>
                <th scope="col">Name</th><th scope="col">Adapter</th><th scope="col">Endpoint</th>
                <th scope="col">State</th><th scope="col">Last test</th><th scope="col">Actions</th>
              </tr></thead>
              <tbody>{connections.map(c => {
                const t = c.lastTest;
                const stale = t !== null && t !== undefined && t.testedRevision !== c.revision;
                return (
                  <tr key={c.id}>
                    <td>{c.name}<br /><small>rev {c.revision}</small></td>
                    <td>{adapterName(c.adapterKey)}</td>
                    <td className="activity-cell-wrap">{c.endpoint}</td>
                    <td>{c.enabled ? 'Enabled' : 'Disabled'}</td>
                    <td className="activity-cell-wrap">
                      {!t ? 'Not tested' : (
                        <>
                          <strong className={healthClass(t.health)}>{t.health}</strong>
                          {' · compatibility '}{t.compatibility}
                          {t.healthReason && <><br /><small>{t.healthReason}</small></>}
                          {t.compatibilityReason && <><br /><small>{t.compatibilityReason}</small></>}
                          {stale && <><br /><small>Tested an older revision.</small></>}
                        </>
                      )}
                    </td>
                    <td>
                      <div className="activity-filter-actions">
                        <button type="button" className="secondary-button" disabled={busy !== null || !c.enabled}
                          onClick={() => void test(c)}>{busy === `test-${c.id}` ? 'Testing…' : 'Test'}</button>
                        <button type="button" className="secondary-button"
                          disabled={busy !== null || !c.enabled || !supportsDiscovery(c.adapterKey)}
                          onClick={() => void discover(c)}>{busy === `discover-${c.id}` ? 'Discovering…' : 'Discover'}</button>
                        <button type="button" className="secondary-button" disabled={busy !== null}
                          onClick={() => setDraft({ id: c.id, name: c.name, adapterKey: c.adapterKey, endpoint: c.endpoint, enabled: c.enabled, revision: c.revision, settings: c.settings ?? {} })}>Edit</button>
                        <button type="button" className="secondary-button" disabled={busy !== null}
                          onClick={() => void remove(c)}>Delete</button>
                      </div>
                    </td>
                  </tr>
                );
              })}</tbody>
            </table>
          </div>
        )}

      {discovery && (
        <>
          <h3 className="access-intro">
            Discovery preview · {discovery.assets.length} found, {pending} not yet imported
          </h3>
          <p className="access-note access-intro">
            Results are limited to what the connection's ForeSite user may see. Wells are placed in the hierarchy
            below; a missing value falls into "Unassigned" / "Unknown type" / "Unspecified device".
          </p>
          <div className="activity-filters">
            <label>Hierarchy template
              <select value={templateKey} onChange={e => setTemplateKey(e.target.value)}>
                {templates.map(x => <option key={x.key} value={x.key}>{x.name}</option>)}
                <option value="custom">Custom…</option>
              </select>
            </label>
            {templateKey === 'custom' && (
              <label>Levels (asset, wellType, deviceType, then well last)
                <input value={customLevels} onChange={e => setCustomLevels(e.target.value)} />
              </label>
            )}
          </div>
          <div className="activity-more">
            <button type="button" className="secondary-button" disabled={busy !== null || pending === 0}
              onClick={() => void apply()}>{busy === 'apply' ? 'Applying…' : `Apply ${pending} asset(s)`}</button>
            {applied && <span role="status"> Created {applied.created}, already present {applied.alreadyPresent}.</span>}
          </div>
          <ul className="access-intro" aria-label="Hierarchy preview">{renderTree(tree)}</ul>
        </>
      )}
    </section>
  );
}

interface HierarchyTemplate { key: string; name: string; levels: string[] }
interface TreeNode { label: string; children: Map<string, TreeNode>; well?: DiscoveredAsset }

const levelFallback: Record<string, string> = { asset: 'Unassigned', wellType: 'Unknown type', deviceType: 'Unspecified device' };

/** Mirrors the server's template placement so the preview matches what Apply creates. */
function buildTree(assets: DiscoveredAsset[], levels: string[]): TreeNode {
  const root: TreeNode = { label: '', children: new Map() };
  for (const asset of assets) {
    let node = root;
    for (const level of levels.slice(0, -1)) {
      const label = asset.metadata[level] || levelFallback[level] || level;
      if (!node.children.has(label)) node.children.set(label, { label, children: new Map() });
      node = node.children.get(label)!;
    }
    node.children.set(`#${asset.externalId}`, { label: asset.displayName, children: new Map(), well: asset });
  }
  return root;
}

function renderTree(node: TreeNode): React.ReactNode {
  return [...node.children.entries()].map(([key, child]) => (
    <li key={key}>
      {child.label}{child.well && <small> · well {child.well.externalId}{child.well.alreadyImported ? ' · imported' : ''}</small>}
      {child.children.size > 0 && <ul>{renderTree(child)}</ul>}
    </li>
  ));
}

