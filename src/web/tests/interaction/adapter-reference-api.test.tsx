import { afterEach, describe, expect, it, vi } from 'vitest';
import { loadAdapterOperations } from '../../src/views/adapterOperations';

const fixture = () => [{ adapterKey: 'example', displayName: 'Example', description: 'Example', capabilities: [
  { key: 'data', displayName: 'Provider-defined data', description: 'Data', contractVersion: '1.0', operationKeys: ['read'],
    workflows: [{ key: 'wf', displayName: 'Reference', purpose: 'Read source', readiness: 'ReferenceOnly', contractVersion: '1.0',
      operationKeys: ['read'], steps: [{ key: 'step', description: 'Retrieve', providerApi: 'GET /source',
        requirement: 'Authorized resource', onFailure: 'Stop' }] }] }],
  operations: [{ key: 'read', displayName: 'Read', description: 'Read', capability: 'ConfigurationData', effect: 'ReadOnly',
    contractVersion: '1.0', timestampSemantics: 'Source', qualitySemantics: 'Unknown',
    request: { id: 'request/v1', fields: [] }, response: { id: 'response/v1', fields: [] },
    limits: { maximumQuantities: 1, maximumValueBytes: 65536, maximumReplyBytes: 262144 },
    documentation: { invocation: 'IAssetConfigurationProvider.ReadConfigurationAsync(request, cancellationToken)', executionBoundary: 'Gateway typed contract',
      requestExampleJson: '{"assetExternalId":"101"}', responseExampleJson: '{"displayName":"Example well"}' } }],
}];
afterEach(() => vi.restoreAllMocks());
describe('Adapter reference API conformance', () => {
  it('loads only static metadata and preserves cancellation/no-store', async () => {
    const fetch = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify(fixture())));
    const signal = new AbortController().signal;
    expect((await loadAdapterOperations(signal))[0]?.capabilities?.[0]?.displayName).toBe('Provider-defined data');
    expect(fetch).toHaveBeenCalledExactlyOnceWith('/api/v1/connections/adapter-operations', { signal, credentials: 'same-origin', cache: 'no-store' });
  });
  it('loads a provider-specific read alongside an adapter with no read capabilities', async () => {
    const body = fixture();
    body[0]!.operations[0]!.capability = 'ExtensionRead';
    body.push({ adapterKey: 'discovery-only', displayName: 'Discovery only', description: 'Discovery',
      capabilities: [], operations: [] });
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify(body)));
    const result = await loadAdapterOperations(new AbortController().signal);
    expect(result[0]?.capabilities?.[0]?.displayName).toBe('Provider-defined data');
    expect(result[1]?.capabilities).toEqual([]);
  });
  it.each(['unknown-ref', 'duplicate-group', 'duplicate-operation', 'future-version', 'malformed-example', 'workflow-unknown-operation', 'workflow-duplicate-step', 'workflow-future-version', 'workflow-executable'])('refuses %s metadata', async kind => {
    const body = fixture();
    if (kind === 'unknown-ref') body[0]!.capabilities[0]!.operationKeys = ['missing'];
    if (kind === 'duplicate-group') body[0]!.capabilities.push(body[0]!.capabilities[0]!);
    if (kind === 'duplicate-operation') body[0]!.operations.push(body[0]!.operations[0]!);
    if (kind === 'future-version') body[0]!.capabilities[0]!.contractVersion = '2.0';
    if (kind === 'malformed-example') body[0]!.operations[0]!.documentation.responseExampleJson = '<html>invalid</html>';
    if (kind === 'workflow-unknown-operation') body[0]!.capabilities[0]!.workflows[0]!.operationKeys = ['missing'];
    if (kind === 'workflow-duplicate-step') body[0]!.capabilities[0]!.workflows[0]!.steps.push(body[0]!.capabilities[0]!.workflows[0]!.steps[0]!);
    if (kind === 'workflow-future-version') body[0]!.capabilities[0]!.workflows[0]!.contractVersion = '2.0';
    if (kind === 'workflow-executable') body[0]!.capabilities[0]!.workflows[0]!.readiness = 'ExecuteUrl';
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify(body)));
    await expect(loadAdapterOperations(new AbortController().signal)).rejects.toMatchObject({ kind: 'invalidResponse' });
  });
});
