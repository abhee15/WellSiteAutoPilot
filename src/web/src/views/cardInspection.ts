/** Interpret only the reviewed ForeSite surface-card schema. Other values stay JSON. */
export type SurfaceCard = {
  id: string; kind: 'CURRENT' | 'FULL'; representation: 'SURFACE'; capturedAtUtc: string;
  source: string; positionUnit: 'in'; loadUnit: 'lb'; position: number[]; load: number[];
};
const text = (value: unknown, maximum: number): value is string =>
  typeof value === 'string' && value.length > 0 && value.length <= maximum && ![...value].some(character => character.charCodeAt(0) < 32);
const channel = (value: unknown): value is number[] => Array.isArray(value) &&
  value.length >= 2 && value.length <= 1024 && value.every(n => typeof n === 'number' && Number.isFinite(n) && Math.abs(n) <= 1_000_000);

export function inspectSurfaceCards(schemaId: string, json: string): SurfaceCard[] | null {
  if (schemaId !== 'foresite.surface-cards/v1' || json.length > 131072) return null;
  try {
    const cards: unknown = JSON.parse(json);
    if (!Array.isArray(cards) || cards.length < 1 || cards.length > 16) return null;
    if (!cards.every(card => card && typeof card === 'object' && text(card.id, 128) &&
      (card.kind === 'CURRENT' || card.kind === 'FULL') && card.representation === 'SURFACE' &&
      text(card.source, 256) && text(card.capturedAtUtc, 64) && /(?:Z|[+-]\d{2}:\d{2})$/u.test(card.capturedAtUtc) &&
      Number.isFinite(Date.parse(card.capturedAtUtc)) && card.positionUnit === 'in' && card.loadUnit === 'lb' &&
      channel(card.position) && channel(card.load) && card.position.length === card.load.length)) return null;
    if (new Set(cards.map(card => card.id)).size !== cards.length) return null;
    return cards;
  } catch { return null; }
}

/** Connect the original paired samples in order; no sorting, smoothing or closing segment. */
export function surfaceCardPlot(card: SurfaceCard) {
  const minPosition = Math.min(...card.position), maxPosition = Math.max(...card.position);
  const minLoad = Math.min(...card.load), maxLoad = Math.max(...card.load);
  const x = (value: number) => minPosition === maxPosition ? 280 : 60 + (value - minPosition) / (maxPosition - minPosition) * 440;
  const y = (value: number) => minLoad === maxLoad ? 130 : 230 - (value - minLoad) / (maxLoad - minLoad) * 200;
  return { minPosition, maxPosition, minLoad, maxLoad,
    points: card.position.map((position, i) => `${x(position)},${y(card.load[i])}`).join(' ') };
}
