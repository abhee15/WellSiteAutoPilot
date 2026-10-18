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
const types = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.svg': 'image/svg+xml', '.woff2': 'font/woff2' };
http.createServer(async (req, res) => {
  const url = new URL(req.url, 'http://x');
  const change = url.pathname.match(/^\/api\/v1\/logic-modules\/[^/]+\/([^/]+)\/(approve|retire)$/);
  if (change && req.method === 'POST') {
    const module = responses['/api/v1/logic-modules'].find(item => item.version === decodeURIComponent(change[1]));
    if (change[2] === 'approve') Object.assign(module, { trustStatus: 'CustomerApproved', isEnabled: true });
    else module.isEnabled = false;
    res.writeHead(200, { 'content-type': 'application/json' }); res.end(JSON.stringify(module)); return;
  }
  if (url.pathname.startsWith('/api/')) {
    const body = responses[url.pathname] ?? [];
    res.writeHead(200, { 'content-type': 'application/json' }); res.end(JSON.stringify(body)); return;
  }
  const file = path.join(dist, url.pathname === '/' ? 'index.html' : url.pathname);
  try { const data = await readFile(file); res.writeHead(200, { 'content-type': types[path.extname(file)] ?? 'application/octet-stream' }); res.end(data); }
  catch { res.writeHead(404); res.end(); }
}).listen(port, '127.0.0.1', () => console.log('mock ready', port));
