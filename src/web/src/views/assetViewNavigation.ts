import { hashFor } from '../routing';

// A navigation intent, not a persistent query or an authorization grant. The Assets
// editor still loads the caller's visible Connections and validates the View on save.
const prefix = 'new-view-for-connection:';
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function createViewFromConnectionHash(connectionId: string): string {
  if (!uuid.test(connectionId)) throw new Error('A valid Connection ID is required.');
  return hashFor('assets', prefix + connectionId);
}

export function viewConnectionFromDetail(detail: string | null): string | null {
  if (!detail?.startsWith(prefix)) return null;
  const id = detail.slice(prefix.length);
  return uuid.test(id) ? id : null;
}
