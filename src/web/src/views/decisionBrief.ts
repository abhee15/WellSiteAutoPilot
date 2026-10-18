import type { RecommendationSummaryResponse } from '../api/generated/v1/types.gen';

const words = (value: string) => value.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/[_-]+/g, ' ').toLowerCase();

/** What is being proposed, as a sentence a person can check against the asset. */
export function proposalSentence(
  item: Pick<RecommendationSummaryResponse, 'quantity' | 'suggestedValue' | 'unit' | 'command' | 'code'>,
  assetName?: string,
): string {
  const where = assetName ? ` on ${assetName}` : '';
  const value = typeof item.suggestedValue === 'number' && Number.isFinite(item.suggestedValue)
    ? `${new Intl.NumberFormat().format(item.suggestedValue)}${item.unit ? ' ' + item.unit : ''}` : null;
  if (value && item.quantity) return `Set ${words(item.quantity)} to ${value}${where}.`;
  if (value) return `Suggested value ${value}${where}.`;
  const command = item.command ?? item.code;
  return command ? `Proposed: ${words(command)}${where}.` : `A change is proposed${where}.`;
}

/** "Expired", "Expires in 3 h", "Expires in 12 min", or null when there is no expiry. */
export function timeLeft(expiresAtUtc: string | null | undefined, nowUtc: number): string | null {
  if (!expiresAtUtc) return null;
  const expires = Date.parse(expiresAtUtc);
  if (!Number.isFinite(expires)) return null;
  const minutes = Math.floor((expires - nowUtc) / 60000);
  if (minutes < 0) return 'Expired';
  if (minutes < 1) return 'Expires in less than a minute';
  if (minutes < 120) return `Expires in ${minutes} min`;
  const hours = Math.floor(minutes / 60);
  return hours < 48 ? `Expires in ${hours} h` : `Expires in ${Math.floor(hours / 24)} days`;
}
