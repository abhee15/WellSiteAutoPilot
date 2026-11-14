import type { AdapterOperationsResponse, ConnectionOperationsResponse } from '../api/generated/v1/types.gen';
import { ConnectionsError } from './connections';

async function read(path: string, signal: AbortSignal): Promise<unknown> {
  const response = await fetch('/api/v1/connections/' + path, { signal, credentials: 'same-origin', cache: 'no-store' });
  if (!response.ok) throw new ConnectionsError(response.status === 401 || response.status === 403 ? 'accessDenied' : 'gatewayUnavailable');
  return response.json();
}
const object = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);
const schema = (value: unknown) => object(value) && typeof value.id === 'string' && Array.isArray(value.fields) &&
  value.fields.every(field => object(field) && typeof field.name === 'string' && typeof field.dataType === 'string' &&
    typeof field.required === 'boolean' && typeof field.description === 'string');

const supportedVersion = (value: unknown) => typeof value === 'string' && /^1(?:\.\d+){1,3}$/.test(value);
function example(value: unknown): boolean {
  if (typeof value !== 'string' || value.length > 65536) return false;
  try { return object(JSON.parse(value)); } catch { return false; }
}
function documentation(value: unknown): boolean {
  return object(value) && typeof value.invocation === 'string' && value.invocation.length > 0 &&
    (value.providerReference == null || typeof value.providerReference === 'string') &&
    typeof value.executionBoundary === 'string' && value.executionBoundary.length > 0 &&
    example(value.requestExampleJson) && example(value.responseExampleJson);
}
function workflows(value: unknown, operationKeys: string[]): boolean {
  if (value == null) return true; // Legacy groups have no workflow metadata.
  if (!Array.isArray(value) || value.length < 1 || value.length > 8) return false;
  const ids = new Set<string>();
  return value.every(workflow => {
    if (!object(workflow) || typeof workflow.key !== 'string' || workflow.key.length < 1 ||
      workflow.key.length > 64 || ids.has(workflow.key) || typeof workflow.displayName !== 'string' ||
      workflow.displayName.length < 1 || workflow.displayName.length > 128 ||
      typeof workflow.purpose !== 'string' || workflow.purpose.length < 1 || workflow.purpose.length > 1024 ||
      !['ReferenceOnly', 'Implemented'].includes(String(workflow.readiness)) ||
      !supportedVersion(workflow.contractVersion) || !Array.isArray(workflow.operationKeys) ||
      workflow.operationKeys.length < 1 || workflow.operationKeys.length > 32 ||
      new Set(workflow.operationKeys).size !== workflow.operationKeys.length ||
      !workflow.operationKeys.every(key => typeof key === 'string' && operationKeys.includes(key)) ||
      !Array.isArray(workflow.steps) || workflow.steps.length < 1 || workflow.steps.length > 12) return false;
    ids.add(workflow.key);
    const stepIds = new Set<string>();
    return workflow.steps.every(step => {
      if (!object(step) || typeof step.key !== 'string' || step.key.length < 1 ||
        step.key.length > 64 || stepIds.has(step.key) ||
        typeof step.description !== 'string' || step.description.length < 1 ||
        step.description.length > 1024 ||
        !['providerApi', 'requirement', 'onFailure'].every(field =>
          step[field] == null || (typeof step[field] === 'string' &&
          step[field].length <= (field === 'providerApi' ? 256 : 1024)))) return false;
      stepIds.add(step.key); return true;
    });
  });
}
function validGroups(adapter: Record<string, unknown>): boolean {
  if (!Array.isArray(adapter.operations) || !Array.isArray(adapter.capabilities)) return false;
  const keys = adapter.operations.flatMap(item => object(item) && typeof item.key === 'string' ? [item.key] : []);
  if (new Set(keys).size !== keys.length) return false;
  const groups = adapter.capabilities;
  const groupKeys = groups.flatMap(item => object(item) && typeof item.key === 'string' ? [item.key] : []);
  return new Set(groupKeys).size === groups.length && groups.every(group => object(group) &&
    typeof group.key === 'string' && group.key.length > 0 && typeof group.displayName === 'string' &&
    typeof group.description === 'string' && supportedVersion(group.contractVersion) &&
    Array.isArray(group.operationKeys) && group.operationKeys.length > 0 &&
    new Set(group.operationKeys).size === group.operationKeys.length &&
    group.operationKeys.every(key => keys.includes(key)) && workflows(group.workflows, group.operationKeys));
}

export async function loadAdapterOperations(signal: AbortSignal): Promise<AdapterOperationsResponse[]> {
  const body = await read('adapter-operations', signal);
  if (!Array.isArray(body) || !body.every(adapter => object(adapter) && typeof adapter.adapterKey === 'string' &&
    typeof adapter.displayName === 'string' && typeof adapter.description === 'string' && Array.isArray(adapter.operations) &&
    adapter.operations.every(operation => object(operation) && typeof operation.key === 'string' &&
      typeof operation.displayName === 'string' && typeof operation.description === 'string' && operation.effect === 'ReadOnly' &&
      ['CurrentData', 'HistoricalData', 'Alarms', 'Calculation', 'ConfigurationData', 'ExtensionRead'].includes(String(operation.capability)) && typeof operation.contractVersion === 'string' &&
      typeof operation.timestampSemantics === 'string' && typeof operation.qualitySemantics === 'string' &&
      schema(operation.request) && schema(operation.response) && (operation.structuredResponse == null || schema(operation.structuredResponse)) && object(operation.limits) &&
      Number.isSafeInteger(operation.limits.maximumQuantities) && Number.isSafeInteger(operation.limits.maximumValueBytes) &&
      Number.isSafeInteger(operation.limits.maximumReplyBytes) &&
      (operation.documentation == null || documentation(operation.documentation))) && validGroups(adapter))) throw new ConnectionsError('invalidResponse');
  return body as AdapterOperationsResponse[];
}

export async function loadConnectionOperations(connectionId: string, signal: AbortSignal): Promise<ConnectionOperationsResponse> {
  const body = await read(encodeURIComponent(connectionId) + '/operations', signal);
  if (!object(body) || body.connectionId !== connectionId || typeof body.adapterKey !== 'string' ||
    !Number.isSafeInteger(body.connectionRevision) || typeof body.state !== 'string' || typeof body.reason !== 'string' ||
    !Array.isArray(body.operationKeys) || !body.operationKeys.every(key => typeof key === 'string')) throw new ConnectionsError('invalidResponse');
  return body as ConnectionOperationsResponse;
}
