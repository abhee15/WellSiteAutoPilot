import type { RecommendationDecision, RecommendationDecisionResponse } from '../api/generated/v1/types.gen';

export type { RecommendationDecision };
export type RecommendationDecisionErrorKind =
  | 'invalid'
  | 'accessDenied'
  | 'notFound'
  | 'conflict'
  | 'unavailable'
  | 'invalidResponse';

export class RecommendationDecisionError extends Error {
  constructor(public readonly kind: RecommendationDecisionErrorKind) {
    super(kind);
    this.name = 'RecommendationDecisionError';
  }
}

const uuidPattern =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * Persist only an explicit, reviewed operator decision. Never issue a direct
 * control command, retry a non-idempotent decision, or assume a 2xx response
 * succeeded without validating its persisted identity and status.
 */
export async function submitRecommendationDecision(
  recommendationId: string,
  decision: RecommendationDecision,
  reason: string,
  signal: AbortSignal,
  fetcher: typeof fetch = fetch,
): Promise<RecommendationDecisionResponse> {
  const normalizedReason = reason.trim();
  if (!uuidPattern.test(recommendationId) ||
      (decision !== 'Approved' && decision !== 'Rejected') ||
      normalizedReason.length < 2 || normalizedReason.length > 1024 ||
      [...normalizedReason].some(character => {
        const code = character.charCodeAt(0);
        return code < 32 || (code >= 127 && code <= 159);
      })) {
    throw new RecommendationDecisionError('invalid');
  }

  const response = await fetcher(
    `/api/v1/recommendations/${encodeURIComponent(recommendationId)}/decision`,
    {
      method: 'POST',
      credentials: 'same-origin',
      cache: 'no-store',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ decision, reason: normalizedReason }),
      signal,
    },
  );
  if (response.status === 401 || response.status === 403) {
    throw new RecommendationDecisionError('accessDenied');
  }
  if (response.status === 404) {
    throw new RecommendationDecisionError('notFound');
  }
  if (response.status === 409) {
    throw new RecommendationDecisionError('conflict');
  }
  if (response.status === 400) {
    throw new RecommendationDecisionError('invalid');
  }
  if (!response.ok) {
    throw new RecommendationDecisionError('unavailable');
  }

  let payload: unknown;
  try {
    payload = await response.json();
  } catch {
    throw new RecommendationDecisionError('invalidResponse');
  }

  if (!payload || typeof payload !== 'object' || Array.isArray(payload)) {
    throw new RecommendationDecisionError('invalidResponse');
  }
  const result = payload as Partial<RecommendationDecisionResponse>;
  if (typeof result.id !== 'string' ||
      result.id.toLowerCase() !== recommendationId.toLowerCase() ||
      result.status !== decision ||
      typeof result.decisionBy !== 'string' || !result.decisionBy ||
      typeof result.decisionAtUtc !== 'string' ||
      Number.isNaN(Date.parse(result.decisionAtUtc))) {
    throw new RecommendationDecisionError('invalidResponse');
  }
  return result as RecommendationDecisionResponse;
}
