export function formatTimestamp(value?: string | null) {
  if (!value) {
    return '—';
  }

  const timestamp = new Date(value);
  return Number.isNaN(timestamp.getTime()) ? value : timestamp.toLocaleString();
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
