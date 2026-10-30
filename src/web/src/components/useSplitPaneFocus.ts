import { useLayoutEffect, useRef } from 'react';
import type { SyntheticEvent } from 'react';

const controls = 'button, a[href], input, select, textarea, [tabindex]';
const visible = (element: HTMLElement | null): element is HTMLElement =>
  Boolean(element?.isConnected && element.getClientRects().length);

/** Keep keyboard position when responsive CSS replaces a browse pane with its details. */
export function useSplitPaneFocus(selection: string | null) {
  const ref = useRef<HTMLDivElement>(null);
  const origin = useRef<HTMLElement | null>(null);
  const originKey = useRef<string | undefined>(undefined);
  const previous = useRef<string | null>(null);

  const remember = (event: SyntheticEvent<HTMLDivElement>) => {
    const target = event.target;
    if (!(target instanceof HTMLElement) || target.closest('.split-selected')) return;
    origin.current = target.closest<HTMLElement>(controls)
      ?? target.closest('tr, li')?.querySelector<HTMLElement>(controls) ?? null;
    originKey.current = origin.current?.dataset.splitFocusKey;
  };

  useLayoutEffect(() => {
    const root = ref.current;
    const detail = root?.querySelector<HTMLElement>('.split-selected') ?? null;
    const browse = root?.querySelector<HTMLElement>('.split-browse') ?? null;
    const wasSelected = previous.current !== null;
    const previousSelection = previous.current;
    previous.current = selection;
    if (!root) return;
    // A newly opened item starts at its top, not wherever the previous one was scrolled to.
    if (selection !== null && selection !== previousSelection) detail?.scrollTo?.({ top: 0 });

    if (selection !== null && visible(detail) && !visible(browse)) {
      detail.focus();
    } else if (selection === null && wasSelected &&
      (document.activeElement === document.body || detail?.contains(document.activeElement))) {
      // The origin may have disappeared through filtering, deletion or lost authorization.
      const destination = visible(origin.current) && root.contains(origin.current) ? origin.current
        : originKey.current ? [...root.querySelectorAll<HTMLElement>('[data-split-focus-key]')]
          .find(element => element.dataset.splitFocusKey === originKey.current) ?? null : null;
      if (visible(destination)) destination.focus();
    }
  }, [selection]);

  return { ref, onFocusCapture: remember, onClickCapture: remember };
}
