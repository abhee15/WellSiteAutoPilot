import type { AdapterOperationsResponse, ConnectionOperationsResponse } from '../api/generated/v1/types.gen';
import { ConnectionsError } from './connections';

async function read(path: string, signal: AbortSignal): Promise<unknown> {
  const response = await fetch('/api/v1/connections/' + path, { signal, credentials: 'same-origin', cache: 'no-store' });
  if (!response.ok) throw new ConnectionsError(response.status === 401 || response.status === 403 ? 'accessDenied' : 'gatewayUnavailable');
  return response.json();
}
const object = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object';
const schema = (value: unknown) => object(value) && typeof value.id === 'string' && Array.isArray(value.fields) &&
  value.fields.every(field => object(field) && typeof field.name === 'string' && typeof field.dataType === 'string' &&
    typeof field.required === 'boolean' && typeof field.description === 'string');

export async function loadAdapterOperations(signal: AbortSignal): Promise<AdapterOperationsResponse[]> {
  const body = await read('adapter-operations', signal);
  if (!Array.isArray(body) || !body.every(adapter => object(adapter) && typeof adapter.adapterKey === 'string' &&
    typeof adapter.displayName === 'string' && typeof adapter.description === 'string' && Array.isArray(adapter.operations) &&
    adapter.operations.every(operation => object(operation) && typeof operation.key === 'string' &&
      typeof operation.displayName === 'string' && typeof operation.description === 'string' && operation.effect === 'ReadOnly' &&
      ['CurrentData', 'HistoricalData'].includes(String(operation.capability)) && typeof operation.contractVersion === 'string' &&
      typeof operation.timestampSemantics === 'string' && typeof operation.qualitySemantics === 'string' &&
      schema(operation.request) && schema(operation.response) && object(operation.limits) &&
      Number.isSafeInteger(operation.limits.maximumQuantities) && Number.isSafeInteger(operation.limits.maximumValueBytes) &&
      Number.isSafeInteger(operation.limits.maximumReplyBytes)))) throw new ConnectionsError('invalidResponse');
  return body as AdapterOperationsResponse[];
}

export async function loadConnectionOperations(connectionId: string, signal: AbortSignal): Promise<ConnectionOperationsResponse> {
  const body = await read(encodeURIComponent(connectionId) + '/operations', signal);
  if (!object(body) || body.connectionId !== connectionId || typeof body.adapterKey !== 'string' ||
    !Number.isSafeInteger(body.connectionRevision) || typeof body.state !== 'string' || typeof body.reason !== 'string' ||
    !Array.isArray(body.operationKeys) || !body.operationKeys.every(key => typeof key === 'string')) throw new ConnectionsError('invalidResponse');
  return body as ConnectionOperationsResponse;
}
