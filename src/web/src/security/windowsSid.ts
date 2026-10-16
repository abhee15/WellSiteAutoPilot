/** Syntax only. Assignment targets are never membership evidence. */
export function isWindowsSid(value: unknown): value is string {
  if (typeof value !== 'string' || value.length > 252) return false;
  const parts = value.split('-');
  const uint = (part: string) => /^(0|[1-9][0-9]{0,9})$/.test(part) && Number(part) <= 4294967295;
  if (parts.length < 4 || parts.length > 18 || parts[0].toUpperCase() !== 'S' || parts[1] !== '1') return false;
  const authority = parts[2];
  return (/^0x[0-9a-f]{12}$/i.test(authority)
    ? Number.parseInt(authority.slice(2), 16) >= 4294967296 : uint(authority)) && parts.slice(3).every(uint);
}
