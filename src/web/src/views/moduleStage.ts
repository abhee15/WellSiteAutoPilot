import type { GuidedModuleDraftResponse } from '../api/generated/v1';

export type ModuleStage = 'notStarted' | 'inProgress' | 'review' | 'approved';

/**
 * Where a module is in its life. Approved means an installed module carries the draft's module id; in review means
 * the draft has a submission waiting for a reviewer. A set that could not be loaded is null and decides nothing, so
 * the module stays "in progress" rather than being called approved on a guess.
 */
export function moduleStage(
  draft: GuidedModuleDraftResponse,
  moduleId: string | undefined,
  installedModuleIds: ReadonlySet<string> | null,
  pendingDraftIds: ReadonlySet<string> | null,
): ModuleStage {
  if (moduleId && installedModuleIds?.has(moduleId)) return 'approved';
  if (draft.id && pendingDraftIds?.has(draft.id)) return 'review';
  return draft.rulesJson || draft.definition ? 'inProgress' : 'notStarted';
}
