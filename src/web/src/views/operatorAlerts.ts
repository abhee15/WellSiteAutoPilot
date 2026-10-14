import type {
  ExecutionResponse,
  SystemInfoResponse,
} from '../api/generated/v1/types.gen';

export type OperatorAlert = {
  id: string;
  category: 'runtime' | 'execution';
  kind: 'attention' | 'unavailable';
  label: string;
  detail: string;
  occurredAtUtc: string | null;
};

const attentionExecutionStatuses = new Set(['failed', 'suspended', 'timedout']);
const unavailableComponentStatuses = new Set(['unavailable', 'failed']);

const MAX_ALERTS = 20;

/**
 * Transient operator awareness only: works exclusively on already-authorized
 * executions and runtime-health responses. This is not a durable alert stream
 * or a replacement for SCADA alarms and must not trigger physical actions.
 */
export function collectOperatorAlerts(
  systemInfo: SystemInfoResponse | null,
  authorizedExecutions: readonly ExecutionResponse[],
): OperatorAlert[] {
  const alerts: OperatorAlert[] = [];
  const seen = new Set<string>();

  for (const component of systemInfo?.components ?? []) {
    const name = component.name?.trim();
    const status = component.status?.trim() ?? '';
    if (!name || !status || status.toLowerCase() === 'healthy') continue;
    const id = 'runtime:' + name;
    if (seen.has(id)) continue;
    seen.add(id);
    alerts.push({
      id,
      category: 'runtime',
      kind: unavailableComponentStatuses.has(status.toLowerCase())
        ? 'unavailable' : 'attention',
      label: name,
      detail: status,
      occurredAtUtc: null,
    });
  }

  for (const execution of authorizedExecutions) {
    const status = execution.status?.trim() ?? '';
    if (!attentionExecutionStatuses.has(status.toLowerCase()) ||
        !execution.executionId) continue;
    const id = 'execution:' + execution.executionId;
    if (seen.has(id)) continue;
    seen.add(id);
    alerts.push({
      id,
      category: 'execution',
      kind: 'attention',
      label: execution.assetExternalId?.trim() ||
        execution.moduleId?.trim() || execution.executionId,
      detail: [execution.moduleId, status, execution.failureCode]
        .filter(Boolean).join(' · '),
      occurredAtUtc: execution.completedAtUtc ?? execution.requestedAtUtc ?? null,
    });
  }

  const when = (value: string | null) => {
    if (!value) return 0;
    const time = Date.parse(value);
    return Number.isFinite(time) ? time : 0;
  };

  return alerts.sort((left, right) => {
    if (left.category !== right.category) {
      return left.category === 'runtime' ? -1 : 1;
    }
    if (left.kind !== right.kind) {
      return left.kind === 'unavailable' ? -1 : 1;
    }
    return when(right.occurredAtUtc) - when(left.occurredAtUtc) ||
      left.id.localeCompare(right.id);
  }).slice(0, MAX_ALERTS);
}
