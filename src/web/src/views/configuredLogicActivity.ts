import type { ConfiguredLogicActivityResponse } from '../api/generated/v1/types.gen';
import { isSecurityId } from '../security/identifiers';
import { readDetail } from './detailRead';

export async function fetchConfigurationActivity(id: string, signal: AbortSignal) {
  const data = await readDetail<ConfiguredLogicActivityResponse>(`/api/v1/configured-logic/${encodeURIComponent(id)}/activity`, signal);
  if (!data || !(data.activeRevisionId === null || isSecurityId(data.activeRevisionId)) ||
    !Number.isFinite(Date.parse(data.observedAtUtc ?? '')) || !Array.isArray(data.recentRuns))
    throw new Error('CONFIGURATION_ACTIVITY_INVALID');
  return data;
}
