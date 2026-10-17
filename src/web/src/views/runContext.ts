import type { AssetResponse, ConfiguredLogicResponse, ExecutionResponse } from '../api/generated/v1/types.gen';

export type RunContext = {
  /** The customer's name for the logic, when the configuration is in the loaded list. */
  logicName: string | null;
  revisionNumber: number | null;
  /** Names of the assets the run covered (falls back to short ids for assets not loaded). */
  assetLabels: string[];
};

const shortId = (id: string) => (id.length > 12 ? `${id.slice(0, 8)}…` : id);

/**
 * Joins a run to the names a person recognises, using the already loaded lists. A run reports only
 * ids; the configuration's saved revision says which assets it was set up for. Anything not in the
 * loaded lists simply stays unnamed (callers fall back to what the run itself reports).
 */
export function describeRunContext(
  execution: Pick<ExecutionResponse, 'logicInstanceId' | 'configurationRevisionId' | 'assetId'>,
  configurations: readonly ConfiguredLogicResponse[],
  assets: readonly AssetResponse[],
): RunContext {
  const configuration = configurations.find(item => item.id === execution.logicInstanceId);
  const revision = configuration?.revisions?.find(item => item.id === execution.configurationRevisionId);
  const assetIds = execution.assetId
    ? [execution.assetId]
    : [...new Set((revision?.assetBindings ?? []).flatMap(binding => (binding.assetId ? [binding.assetId] : [])))];
  const names = new Map(assets.flatMap(asset => (asset.id ? [[asset.id, asset.name ?? ''] as const] : [])));
  return {
    logicName: configuration?.name ?? null,
    revisionNumber: revision?.revisionNumber ?? null,
    assetLabels: assetIds.map(id => names.get(id) || shortId(id)),
  };
}

/** "North Well", "North Well, South Well" or "North Well +2 more". */
export function summarizeAssetLabels(labels: readonly string[], maximum = 2): string {
  if (labels.length <= maximum) return labels.join(', ');
  return `${labels.slice(0, maximum).join(', ')} +${labels.length - maximum} more`;
}
