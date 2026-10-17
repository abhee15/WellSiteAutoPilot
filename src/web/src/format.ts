export function formatTimestamp(value?: string | null) {
  if (!value) {
    return '—';
  }

  const timestamp = new Date(value);
  return Number.isNaN(timestamp.getTime()) ? value : timestamp.toLocaleString(undefined, { timeZoneName: 'short' });
}

export function shortId(value?: string | null) {
  if (!value) {
    return '—';
  }

  return value.length > 12 ? `${value.slice(0, 8)}…` : value;
}

export function toCssToken(value?: string | null) {
  return (value ?? 'unknown')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-');
}

/** A clock time with its zone, for "Updated" lines. */
export function formatClock(value: Date): string {
  return value.toLocaleTimeString(undefined, { timeZoneName: 'short' });
}
