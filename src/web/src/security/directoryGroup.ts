export function isDirectoryId(value: unknown): value is string {
  return typeof value === 'string' && /^[a-z0-9][a-z0-9.-]{0,63}$/.test(value);
}
export function isGroupId(value: unknown): value is string {
  return typeof value === 'string' && value.length >= 1 && value.length <= 256 &&
    value.trim() === value && !Array.from(value).some(c => c.charCodeAt(0) < 32 || (c.charCodeAt(0) >= 127 && c.charCodeAt(0) <= 159));
}
export function groupSubjectKey(directoryId: string, groupId: string): string {
  if (!isDirectoryId(directoryId) || !isGroupId(groupId)) throw new Error('Invalid directory group');
  return directoryId + ':' + groupId;
}
export function parseGroupSubjectKey(value: string): { directoryId: string; groupId: string } | null {
  const split = value.indexOf(':');
  if (split < 1) return null;
  const directoryId = value.slice(0, split), groupId = value.slice(split + 1);
  return isDirectoryId(directoryId) && isGroupId(groupId) ? { directoryId, groupId } : null;
}
