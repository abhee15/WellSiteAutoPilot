/** Only structured, definite server refusals have a known mutation outcome. */
export type SecurityRefusal = 'selfAccess' | 'groupProofRequired' | 'lastDirectAdmin';
export async function readSecurityRefusal(response: Response): Promise<SecurityRefusal | null> {
  if (response.status !== 403 && response.status !== 409) return null;
  let body: unknown;
  try { body = await response.json(); } catch { return null; }
  if (!body || typeof body !== 'object' || Array.isArray(body) || !('code' in body)) return null;
  if (response.status === 403) {
    if (body.code === 'SECURITY_SELF_ACCESS_CHANGE_FORBIDDEN') return 'selfAccess';
    if (body.code === 'SECURITY_GROUP_MEMBERSHIP_REQUIRED') return 'groupProofRequired';
  }
  return response.status === 409 && body.code === 'SECURITY_LAST_DIRECT_ADMIN_REQUIRED'
    ? 'lastDirectAdmin' : null;
}
