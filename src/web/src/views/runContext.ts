import type { AssetResponse, ConfiguredLogicResponse, ExecutionResponse } from '../api/generated/v1/types.gen';
import type { OwnerGroup } from './assetHierarchy';
import { configurationRevision } from './configuredLogicViews';

export type RunContext = {
  /** The customer's name for the logic, when the configuration is in the loaded list. */
  logicName: string | null;
  revisionNumber: number | null;
  /** Names of the assets the run covered (falls back to short ids for assets not loaded). */
  assetLabels: string[];
  /** The same assets by id, in the same order, so a page can link to them. */
  assetIds: string[];
};

export const shortId = (id: string) => (id.length > 12 ? `${id.slice(0, 8)}…` : id);

/**
 * Joins a run to the names a person recognises, using the already loaded lists. A run reports only
 * ids; the configuration's saved revision says which assets it was set up for. Anything not in the
 * loaded lists simply stays unnamed (callers fall back to what the run itself reports).
 */
export function describeRunContext(
  execution: Pick<ExecutionResponse, 'logicInstanceId' | 'configurationRevisionId' | 'assetId'>,
  configurations: readonly ConfiguredLogicResponse[],
  assets: readonly AssetResponse[],
  owners: readonly OwnerGroup[] = [],
): RunContext {
  const configuration = configurations.find(item => item.id === execution.logicInstanceId);
  const revision = configuration?.revisions?.find(item => item.id === execution.configurationRevisionId);
  const assetIds = execution.assetId
    ? [execution.assetId]
    : [...new Set((revision?.assetBindings ?? []).flatMap(binding => (binding.assetId ? [binding.assetId] : [])))];
  return {
    logicName: configuration?.name ?? null,
    revisionNumber: revision?.revisionNumber ?? null,
    assetLabels: assetIds.map(id => assetIdentityLabel(id, assets, owners)),
    assetIds,
  };
}

/** "North Well", "North Well, South Well" or "North Well +2 more". */
export function summarizeAssetLabels(labels: readonly string[], maximum = 2): string {
  if (labels.length <= maximum) return labels.join(', ');
  return `${labels.slice(0, maximum).join(', ')} +${labels.length - maximum} more`;
}

/** Consistent asset labeling for both Overview and Operations, including configured multi-asset runs. */
export function executionAssetLabel(
  execution: Pick<ExecutionResponse, 'assetExternalId' | 'assetId'>,
  context: Pick<RunContext, 'assetLabels'>,
): string {
  if (execution.assetExternalId) return execution.assetExternalId;
  if (context.assetLabels.length > 0) return summarizeAssetLabels(context.assetLabels);
  return execution.assetId ? shortId(execution.assetId) : '—';
}

/** Names of every asset any saved revision of the configuration is set up for. */
export function configurationAssetLabels(
  configuration: ConfiguredLogicResponse,
  assets: readonly AssetResponse[],
): string[] {
  const ids = [...new Set((configuration.revisions ?? []).flatMap(revision =>
    (revision.assetBindings ?? []).flatMap(binding => (binding.assetId ? [binding.assetId] : []))))];
  const names = new Map(assets.flatMap(asset => (asset.id ? [[asset.id, asset.name ?? ''] as const] : [])));
  return ids.map(id => names.get(id) || shortId(id));
}

/**
 * The assets currently targeted by a configuration. Historical revisions must not be combined
 * into the Logic grid: they may refer to assets that this configuration no longer uses.
 */
export function configurationTargetAssetIds(configuration: ConfiguredLogicResponse): string[] {
  const revision = configurationRevision(configuration);
  return [...new Set((revision?.assetBindings ?? []).flatMap(binding =>
    binding.assetId ? [binding.assetId] : []))];
}

/** User-facing asset identity; a source name only comes from that asset's owning Connection. */
export function assetIdentityLabel(id: string | null | undefined,
  assets: readonly AssetResponse[], owners: readonly OwnerGroup[]): string {
  if (!id) return '—';
  const asset = assets.find(item => item.id === id);
  if (!asset?.name) return shortId(id);
  const connectionName = (item: AssetResponse) => owners.find(owner =>
    owner.connectionId && owner.connectionId === item.ownerConnectionId)?.name ?? null;
  const source = connectionName(asset);
  const label = source ? `${asset.name} (${source})` : asset.name;
  // If the owning source cannot be named, two otherwise distinct assets may still
  // have identical labels. Append the short ID only when the displayed labels collide.
  const ambiguous = assets.some(other => other.id !== id && other.name === asset.name &&
    connectionName(other) === source);
  return ambiguous ? `${label} · ${shortId(id)}` : label;
}

/** Use the same label and truncation in Logic, Schedules and revision details. */
export function configuredTargetLabel(
  assetIds: readonly string[], assets: readonly AssetResponse[], owners: readonly OwnerGroup[], maximum = 2,
): string {
  // Preserve the full count without resolving thousands of off-screen asset names.
  if (assetIds.length === 0) return '—';
  const labels = assetIds.slice(0, maximum).map(id => assetIdentityLabel(id, assets, owners));
  return assetIds.length > maximum
    ? `${labels.join(', ')} +${assetIds.length - maximum} more`
    : labels.join(', ');
}

/** Newest recorded run per configuration, from whatever runs are already loaded. */
export function latestRunByConfiguration(
  executions: readonly Pick<ExecutionResponse, 'logicInstanceId' | 'requestedAtUtc' | 'status'>[],
): Map<string, { requestedAtUtc: string; status: string | null }> {
  const latest = new Map<string, { requestedAtUtc: string; status: string | null; time: number }>();
  for (const run of executions) {
    if (!run.logicInstanceId || !run.requestedAtUtc) continue;
    const time = Date.parse(run.requestedAtUtc);
    if (!Number.isFinite(time)) continue;
    const current = latest.get(run.logicInstanceId);
    if (!current || time > current.time) {
      latest.set(run.logicInstanceId, { requestedAtUtc: run.requestedAtUtc, status: run.status ?? null, time });
    }
  }
  return new Map([...latest].map(([id, { requestedAtUtc, status }]) => [id, { requestedAtUtc, status }]));
}
