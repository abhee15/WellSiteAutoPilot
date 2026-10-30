import { useEffect } from 'react';
import type { RefObject } from 'react';

/** Name of the CSS variable that holds the distance from the top of the page to the master-detail panes. */
export const PANE_TOP_VARIABLE = '--pane-top';

/**
 * Master-detail pages (Runs, Activity, Assets, Logic, ...) give each pane the height that is left in the window
 * and let the panes scroll on their own, instead of the whole page scrolling. The CSS cannot know how tall the
 * page header, banners and tabs above the panes are, so this measures where the panes start and publishes it as
 * `--pane-top`. It follows layout changes (a banner appearing, a tab change, a resize) and cleans up after itself.
 */
export function useSplitPaneViewport(root: RefObject<HTMLElement | null>): void {
  useEffect(() => {
    const element = root.current;
    if (!element) return;
    const style = document.documentElement.style;
    let frame = 0;

    const measure = () => {
      frame = 0;
      const panes = element.querySelector<HTMLElement>('.split-panes');
      if (!panes) {
        style.removeProperty(PANE_TOP_VARIABLE);
        return;
      }
      const top = Math.round(panes.getBoundingClientRect().top + window.scrollY);
      if (style.getPropertyValue(PANE_TOP_VARIABLE) !== `${top}px`) style.setProperty(PANE_TOP_VARIABLE, `${top}px`);
    };
    const schedule = () => { if (!frame) frame = requestAnimationFrame(measure); };

    const resize = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(schedule);
    resize?.observe(element);
    const mutation = new MutationObserver(schedule);
    mutation.observe(element, { childList: true, subtree: true });
    window.addEventListener('resize', schedule);
    measure();

    return () => {
      if (frame) cancelAnimationFrame(frame);
      resize?.disconnect();
      mutation.disconnect();
      window.removeEventListener('resize', schedule);
      style.removeProperty(PANE_TOP_VARIABLE);
    };
  }, [root]);
}
