import axe from 'axe-core';
import type { RunOptions } from 'axe-core';

/**
 * Runs axe on rendered markup and returns readable violation lines (empty when clean).
 * jsdom has no layout or paint, so the checks that need them (contrast, landmark regions of a page fragment) are
 * left to the browser; everything about names, roles, labels, ids and structure is checked here.
 */
// axe probes canvas support for an icon-ligature check; jsdom has none and logs a warning, so say "no canvas" quietly.
HTMLCanvasElement.prototype.getContext = (() => null) as never;

export async function accessibilityViolations(root: Element, extra: RunOptions = {}): Promise<string[]> {
  const result = await axe.run(root, {
    rules: { 'color-contrast': { enabled: false }, region: { enabled: false } },
    ...extra,
  });
  return result.violations.map(violation =>
    `${violation.id}: ${violation.help} (${violation.nodes.map(node => node.target.join(' ')).join(', ')})`);
}
