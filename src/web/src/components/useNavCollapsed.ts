import { useCallback, useEffect, useState } from 'react';

const STORAGE_KEY = 'wsa.nav.collapsed';
const NARROW_QUERY = '(width < 48em)';

function readStored(): boolean {
  try { return window.localStorage.getItem(STORAGE_KEY) === '1'; } catch { return false; }
}

function narrowMatch(): MediaQueryList | null {
  return typeof window !== 'undefined' && typeof window.matchMedia === 'function' ? window.matchMedia(NARROW_QUERY) : null;
}

/**
 * Whether the side panel is shown as a narrow icon rail. The person's choice is remembered in this
 * browser (default: expanded); narrow windows always use the rail so the page keeps its room.
 */
export function useNavCollapsed() {
  const [preferred, setPreferred] = useState(readStored);
  const [narrow, setNarrow] = useState(() => narrowMatch()?.matches ?? false);

  useEffect(() => {
    const query = narrowMatch();
    if (!query) return undefined;
    const update = () => setNarrow(query.matches);
    query.addEventListener('change', update);
    return () => query.removeEventListener('change', update);
  }, []);

  const toggle = useCallback(() => {
    setPreferred(previous => {
      const next = !previous;
      try { window.localStorage.setItem(STORAGE_KEY, next ? '1' : '0'); } catch { /* the choice just is not remembered */ }
      return next;
    });
  }, []);

  return { collapsed: preferred || narrow, narrow, toggle };
}
