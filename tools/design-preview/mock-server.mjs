import http from 'node:http';
import { readFile } from 'node:fs/promises';
import path from 'node:path';

const dist = process.argv[2];
const port = Number(process.argv[3] ?? 5199);
const id = n => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const now = Date.now();
const iso = minutes => new Date(now - minutes * 60_000).toISOString();

const typeWell = id(1), typePad = id(2);
const assets = [
  { id: id(10), assetTypeId: typePad, name: 'Permian Pad 7', parentAssetId: null, attributeValuesJson: '{}', isActive: true, createdAtUtc: iso(9000) },
  { id: id(11), assetTypeId: typeWell, name: 'Well 7-12H', parentAssetId: id(10), attributeValuesJson: '{"liftType":"ESP"}', isActive: true, createdAtUtc: iso(8000) },
  { id: id(12), assetTypeId: typeWell, name: 'Well 7-14H', parentAssetId: id(10), attributeValuesJson: '{"liftType":"Gas lift"}', isActive: true, createdAtUtc: iso(7000) },
  { id: id(13), assetTypeId: typeWell, name: 'Well 3-02', parentAssetId: null, attributeValuesJson: '{}', isActive: false, createdAtUtc: iso(6000) },
];
const espManifest = { moduleId: 'esp-frequency-optimizer', version: '1.2.0',
      executionProfile: 'ScheduledOneShot', description: 'Keeps an ESP near its best operating frequency.',
      assetRequirements: [{ role: 'well', minimumCount: 1, maximumCount: 1, requiredAssetTypeKey: 'well' }], dataRequirements: [],
      parameterSchema: { type: 'object', required: ['targetFrequencyHz'], properties: {
        targetFrequencyHz: { type: 'number', minimum: 30, maximum: 70, title: 'Target frequency (Hz)', description: 'Where the optimizer aims.' },
        mode: { type: 'string', enum: ['Hold', 'Trim'] }, allowIncrease: { type: 'boolean' } } } };
const exec = (n, mode, status, result, minutes, asset) => ({
  executionId: id(100 + n), logicInstanceId: id(200), moduleId: 'esp-frequency-optimizer', moduleVersion: '1.2.0',
  configurationRevisionId: id(300), assetId: asset, assetExternalId: null, quantity: null, requestContractVersion: 2,
  trigger: 'Scheduled', scheduledForUtc: iso(minutes), mode, status, correlationId: `corr-${n}`,
  requestedAtUtc: iso(minutes), startedAtUtc: iso(minutes - 0.1), completedAtUtc: status === 'Completed' ? iso(minutes - 0.3) : null,
  resultCode: result, failureCode: status === 'Failed' ? 'PROVIDER_TIMEOUT' : null, outputJson: '{}',
});
const configuredDetail = { id: id(500), name: 'ESP frequency — Permian Pad 7', activeRevisionId: id(300), canManage: true, canRequestShadow: true,
  createdAtUtc: iso(5000), revisions: [{ id: id(300), configuredLogicId: id(500), revisionNumber: 2, moduleId: 'esp-frequency-optimizer', moduleVersion: '1.2.0',
    moduleManifestJson: JSON.stringify(espManifest), parametersJson: '{"targetFrequencyHz":52.5,"mode":"Trim","allowIncrease":true}', mode: 'Shadow', status: 'Active',
    createdAtUtc: iso(3000), assetBindings: [{ role: 'well', assetId: id(11), parameterOverridesJson: '{}' }], dataBindings: [] }] };
const executions = [
  exec(1, 'Shadow', 'Completed', 'RecommendationProduced', 3, id(11)),
  exec(2, 'Shadow', 'Completed', 'NoAction', 13, id(12)),
  exec(3, 'Shadow', 'Failed', null, 23, id(11)),
  exec(4, 'Shadow', 'Running', null, 1, id(12)),
];
const responses = {
  '/api/v1/security/me': { userId: id(900), identityName: 'FIELD\\10482267', displayName: 'Alex Rivera', isActive: true,
    permissions: ['assets.read', 'assets.manage', 'configured-logic.read', 'configured-logic.manage', 'executions.read',
      'executions.request', 'recommendations.read', 'recommendations.decide', 'logic.read', 'logic.manage', 'security.manage'] },
  '/api/v1/system/info': { product: 'WellSite AutoPilot', components: [
    { name: 'Server', version: '0.1.0', status: 'Healthy' }, { name: 'Integration Gateway', version: '0.1.0', status: 'Healthy' },
    { name: '.NET Worker', version: '0.1.0', status: 'Healthy' }, { name: 'NATS JetStream', version: '2.11', status: 'Degraded' }] },
  '/api/v1/asset-types': [
    { id: typeWell, key: 'well', displayName: 'Well', schemaVersion: 1, attributeSchemaJson: '{}', isActive: true, createdAtUtc: iso(9500) },
    { id: typePad, key: 'pad', displayName: 'Pad', schemaVersion: 1, attributeSchemaJson: '{}', isActive: true, createdAtUtc: iso(9500) }],
  '/api/v1/assets': assets,
  '/api/v1/assets/capabilities': { canCreateRoot: true, canCreateUnderAuthorizedParents: true },
  '/api/v1/executions': executions,
  '/api/v1/logic-modules': [{ id: id(400), moduleId: 'esp-frequency-optimizer', version: '1.2.0', displayName: 'ESP frequency optimizer',
    publisher: 'Weatherford', runtime: 'DotNet', executionProfile: 'Shadow', manifestJson: JSON.stringify(espManifest), packageSha256: 'ab'.repeat(32),
    trustStatus: 'Trusted', isEnabled: true, installedAtUtc: iso(5000) },
  { id: id(401), moduleId: 'esp-frequency-optimizer', version: '1.3.0', displayName: 'ESP frequency optimizer',
    publisher: 'Weatherford', runtime: 'DotNet', executionProfile: 'Shadow', manifestJson: JSON.stringify(espManifest), packageSha256: 'cd'.repeat(32),
    trustStatus: 'Untrusted', isEnabled: false, installedAtUtc: iso(60) },
  { id: id(402), moduleId: 'esp-frequency-optimizer', version: '1.0.0', displayName: 'ESP frequency optimizer',
    publisher: 'Weatherford', runtime: 'DotNet', executionProfile: 'Shadow', manifestJson: JSON.stringify(espManifest), packageSha256: 'ef'.repeat(32),
    trustStatus: 'CustomerApproved', isEnabled: false, installedAtUtc: iso(20000) }],
  '/api/v1/security/users': [
    { userId: id(900), identityName: 'FIELD\\10482267', displayName: 'Alex Rivera', isActive: true, createdAtUtc: iso(9000), lastSeenAtUtc: iso(1), activeDirectGrantCount: 2 },
    { userId: id(901), identityName: 'FIELD\\10551903', displayName: null, isActive: true, createdAtUtc: iso(30), lastSeenAtUtc: iso(5), activeDirectGrantCount: 0 },
    { userId: id(902), identityName: 'FIELD\\10337741', displayName: 'Morgan Lee', isActive: true, createdAtUtc: iso(5000), lastSeenAtUtc: iso(90), activeDirectGrantCount: 1 },
    { userId: id(903), identityName: 'FIELD\\10299120', displayName: 'Sam Patel', isActive: false, createdAtUtc: iso(6000), lastSeenAtUtc: iso(4000), activeDirectGrantCount: 0 }],
  '/api/v1/configured-logic': [configuredDetail],
  ['/api/v1/configured-logic/' + id(500)]: configuredDetail,
  '/api/v1/recommendations': [],
};
// Optional live-update hub (MOCK_HUB=1): just enough of SignalR's long-polling transport for the
// browser client to connect. Without it /hubs/changes is a 404 and the page falls back to polling,
// which is also worth previewing. POST /__mock/change?area=assets pushes a change signal.
const hubEnabled = process.env.MOCK_HUB === '1';
const RS = '\u001e';
const hubConnections = new Map();
const hubSend = (connection, text) => {
  if (connection.waiting) { const waiting = connection.waiting; connection.waiting = null; waiting.writeHead(200); waiting.end(text); }
  else connection.queue.push(text);
};
function handleHub(req, res, url) {
  const send = (status, body, type = 'application/json') => { res.writeHead(status, { 'content-type': type }); res.end(body); };
  if (url.pathname === '/hubs/changes/negotiate' && req.method === 'POST') {
    const token = 'mock-' + Math.random().toString(36).slice(2);
    hubConnections.set(token, { queue: [], waiting: null, started: false });
    send(200, JSON.stringify({ negotiateVersion: 1, connectionId: token + '-id', connectionToken: token,
      availableTransports: [{ transport: 'LongPolling', transferFormats: ['Text', 'Binary'] }] }));
    return;
  }
  const connection = hubConnections.get(url.searchParams.get('id') ?? '');
  if (url.pathname !== '/hubs/changes' || !connection) { send(404, '{}'); return; }
  if (req.method === 'GET') {
    if (!connection.started) { connection.started = true; res.writeHead(200); res.end(); return; }
    if (connection.queue.length) { res.writeHead(200); res.end(connection.queue.splice(0).join('')); return; }
    connection.waiting = res; setTimeout(() => { if (connection.waiting === res) { connection.waiting = null; res.writeHead(200); res.end(); } }, 30_000);
    return;
  }
  if (req.method === 'DELETE') { hubConnections.delete(url.searchParams.get('id')); send(202, ''); return; }
  let body = ''; req.on('data', chunk => { body += chunk; });
  req.on('end', () => {
    if (body.includes('"protocol"')) hubSend(connection, '{}' + RS);
    send(200, '');
  });
}
const types = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.svg': 'image/svg+xml', '.woff2': 'font/woff2' };
http.createServer(async (req, res) => {
  const url = new URL(req.url, 'http://x');
  if (hubEnabled && url.pathname === '/__mock/change' && req.method === 'POST') {
    const area = url.searchParams.get('area') ?? 'assets';
    for (const connection of hubConnections.values()) {
      hubSend(connection, JSON.stringify({ type: 1, target: 'changed', arguments: [area] }) + RS);
    }
    res.writeHead(204); res.end(); return;
  }
  if (hubEnabled && url.pathname.startsWith('/hubs/changes')) { handleHub(req, res, url); return; }
  const change = url.pathname.match(/^\/api\/v1\/logic-modules\/[^/]+\/([^/]+)\/(approve|retire)$/);
  if (change && req.method === 'POST') {
    const module = responses['/api/v1/logic-modules'].find(item => item.version === decodeURIComponent(change[1]));
    if (change[2] === 'approve') Object.assign(module, { trustStatus: 'CustomerApproved', isEnabled: true });
    else module.isEnabled = false;
    res.writeHead(200, { 'content-type': 'application/json' }); res.end(JSON.stringify(module)); return;
  }
  if (url.pathname === '/api/v1/security/audit-events') {
    // Recent events, newest first; the cursor is just the next offset. The first ones mirror a real session:
    // a connection flapping, then one person creating, validating and activating logic and approving a module.
    const person = { actorUserId: id(900), actorIdentity: 'FIELD\\10482267', actorName: 'Sri Mallepudi' };
    const monitor = { actorUserId: null, actorIdentity: 'system:connection-monitor', actorName: null };
    const publisher = { actorUserId: null, actorIdentity: 'system:package-publisher', actorName: null };
    const connection = { targetType: 'connection', targetId: id(501), targetName: 'ForeSite', assetId: null, correlationId: null };
    const logic = { targetType: 'ConfiguredLogic', targetId: id(502), targetName: 'Gas lift optimizer', assetId: null, correlationId: 'c0ffee-4f2a' };
    const module = { targetType: 'logic-module', targetId: id(503), targetName: 'ForeSite well test v1.2.0', assetId: null, correlationId: null };
    const flip = (from, to) => JSON.stringify({ AdapterKey: 'foresite', From: from, To: to });
    const recent = [
      { ...monitor, ...connection, minutes: 2, action: 'connection.status_changed', detailsJson: flip('Unreachable', 'Healthy') },
      { ...monitor, ...connection, minutes: 63, action: 'connection.status_changed', detailsJson: flip('Healthy', 'Unreachable') },
      { ...monitor, ...connection, minutes: 175, action: 'connection.status_changed', detailsJson: flip('Unreachable', 'Healthy') },
      { ...monitor, ...connection, minutes: 186, action: 'connection.status_changed', detailsJson: flip('Healthy', 'Unreachable') },
      { ...person, ...logic, minutes: 1250, action: 'CONFIGURED_LOGIC_REVISION_ACTIVATED', detailsJson: JSON.stringify({ RevisionNumber: 1 }) },
      { ...person, ...logic, minutes: 1250, action: 'CONFIGURED_LOGIC_REVISION_VALIDATED', detailsJson: JSON.stringify({ RevisionNumber: 1 }) },
      { ...person, ...logic, minutes: 1250, action: 'CONFIGURED_LOGIC_CREATED', detailsJson: JSON.stringify({ RevisionNumber: 1 }) },
      { ...publisher, ...module, minutes: 1251, action: 'logic-module.package-published', detailsJson: '{}' },
      { ...person, ...module, minutes: 1251, action: 'logic-module.approved', detailsJson: JSON.stringify({ reason: 'Reviewed against the vendor release notes.' }) },
    ].map((item, index) => ({ id: id(6000 + index), occurredAtUtc: iso(item.minutes), ...item }));
    recent.forEach(item => { delete item.minutes; });
    const kinds = [
      ['security.grant.created', 'security-access-grant', { scopeKind: 'ExactAssets', assetCount: 3 }],
      ['asset.created', 'asset', {}],
      ['recommendation.approved', 'recommendation', { note: 'Within operating limits.' }],
      ['security.role.updated', 'security-role', { revision: 4 }],
      ['future.unlabelled-action', 'Unknown', { nested: { ignored: true } }]];
    const older = Array.from({ length: 120 }, (_, index) => {
      const [action, targetType, details] = kinds[index % kinds.length];
      const named = targetType === 'asset' ? 'Well 14-22' : targetType === 'security-role' ? 'Field engineer' : null;
      return { id: id(7000 + index), occurredAtUtc: iso(2000 + index * 37), actorUserId: id(900),
        actorIdentity: index % 4 === 3 ? 'FIELD\\10337741' : 'FIELD\\10482267',
        actorName: index % 4 === 3 ? null : 'Sri Mallepudi', action, targetType,
        targetId: id(8000 + index), targetName: named, assetId: null, correlationId: null, detailsJson: JSON.stringify(details) };
    });
    const all = [...recent, ...older];
    const prefix = url.searchParams.get('actionPrefix') ?? '';
    const type = url.searchParams.get('targetType') ?? '';
    const limit = Math.min(Number(url.searchParams.get('limit') ?? 50), 50);
    const offset = url.searchParams.get('cursor') ? Number(Buffer.from(url.searchParams.get('cursor'), 'base64url').toString()) : 0;
    const matches = all.filter(item => item.action.startsWith(prefix) && (!type || item.targetType === type));
    const items = matches.slice(offset, offset + limit);
    const next = offset + limit < matches.length ? Buffer.from(String(offset + limit)).toString('base64url') : null;
    res.writeHead(200, { 'content-type': 'application/json' }); res.end(JSON.stringify({ items, nextCursor: next })); return;
  }
  if (url.pathname.startsWith('/api/')) {
    const body = responses[url.pathname] ?? [];
    res.writeHead(200, { 'content-type': 'application/json' }); res.end(JSON.stringify(body)); return;
  }
  const file = path.join(dist, url.pathname === '/' ? 'index.html' : url.pathname);
  try { const data = await readFile(file); res.writeHead(200, { 'content-type': types[path.extname(file)] ?? 'application/octet-stream' }); res.end(data); }
  catch { res.writeHead(404); res.end(); }
}).listen(port, '127.0.0.1', () => console.log('mock ready', port));
