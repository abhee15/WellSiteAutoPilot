/**
 * Returns `current` when `next` holds exactly the same data, so a refresh that found nothing new does not
 * hand every component below a fresh copy to rebuild from. Only JSON-shaped data (what the Server sends)
 * is meant to go through here. Any difference at all, including a different key order, returns `next`,
 * so the worst case is the redraw the page always did.
 */
export function keepIfSame<T>(current: T, next: T): T {
  if (current === next) return current;
  try {
    return JSON.stringify(current) === JSON.stringify(next) ? current : next;
  } catch {
    return next;
  }
}
