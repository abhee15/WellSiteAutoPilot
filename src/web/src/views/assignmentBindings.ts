import { setupResponse } from './configuredLogicSetup';

export type AssignmentBinding = {
  requirementId: string; assetId: string; providerId: string;
  providerAssetExternalId: string; providerMappingJson: string;
};

/** The server resolves imported identities and validates the module requirements in the caller's asset scope. */
export async function resolveAssignmentBindings(
  moduleManifestJson: string, targets: Record<string, string[]>, signal: AbortSignal,
): Promise<AssignmentBinding[]> {
  const assetBindings = Object.entries(targets).flatMap(([role, ids]) =>
    ids.map(assetId => ({ role, assetId, parameterOverridesJson: '{}' })));
  if (!assetBindings.length) return [];
  const response = await fetch('/api/v1/configured-logic/resolve-inputs', {
    method: 'POST', credentials: 'same-origin', cache: 'no-store', signal,
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ moduleManifestJson, assetBindings }),
  });
  const payload = await setupResponse(response, 200, false);
  if (!Array.isArray(payload) || payload.some(item => !item || typeof item.requirementId !== 'string' ||
      typeof item.assetId !== 'string' || typeof item.providerId !== 'string' ||
      typeof item.providerAssetExternalId !== 'string' || typeof item.providerMappingJson !== 'string')) {
    throw new Error('Invalid input resolution response');
  }
  return payload as AssignmentBinding[];
}
