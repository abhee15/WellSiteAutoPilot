export class DetailReadError extends Error {
  readonly status: number;
  constructor(status: number) { super('DETAIL_READ_FAILED'); this.status = status; }
}

export async function readDetail<T>(url: string, signal: AbortSignal): Promise<T> {
  const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store', signal });
  if (!response.ok) throw new DetailReadError(response.status);
  return response.json() as Promise<T>;
}
