import { useCallback, useEffect, useRef, useState } from 'react';
import type { Section } from './components/NavigationLink';
import type { CurrentUserResponse } from './api/generated/v1/types.gen';
import { canOpenSection } from './security/sessionAccess';

/** Sections in landing priority order. */
export const SECTION_ORDER: readonly Section[] = ['overview', 'operations', 'recommendations', 'assets', 'logic', 'connections', 'access', 'status'];

/**
 * Addresses look like `#section` or `#section/detail` (a selected record or tab).
 * The detail is whatever the section decides it means; it is never a security control.
 */
export function parseHash(
  hash: string = typeof window === 'undefined' ? '' : window.location.hash,
): { section: string; detail: string | null } {
  const [section = '', ...rest] = hash.replace(/^#/, '').split('/');
  let detail: string | null = null;
  if (rest.length > 0 && rest.join('/') !== '') {
    try { detail = decodeURIComponent(rest.join('/')); } catch { detail = null; }
  }
  return { section: section.toLowerCase(), detail };
}

export function hashFor(section: Section, detail?: string | null): string {
  return '#' + section + (detail ? '/' + encodeURIComponent(detail) : '');
}

export function sectionFromHash(): Section {
  const { section } = parseHash();
  return (SECTION_ORDER as readonly string[]).includes(section) ? section as Section : 'overview';
}

/** True when the address names a section; false for an empty or unknown hash (the default landing). */
export function hasExplicitSection(): boolean {
  return (SECTION_ORDER as readonly string[]).includes(parseHash().section);
}

/** The first section this user may open, or null when they may open none. */
export function firstOpenableSection(user: CurrentUserResponse | null | undefined): Section | null {
  return SECTION_ORDER.find(section => canOpenSection(user, section)) ?? null;
}

type DetailUpdate = string | null | ((current: string | null) => string | null);

/**
 * The address-backed detail (selected record or tab) of one section. Changing it adds a browser
 * history entry, so Back returns to the previous selection and the address can be shared.
 */
export function useHashDetail(section: Section): [string | null, (next: DetailUpdate) => void] {
  const read = () => {
    const parsed = parseHash();
    return parsed.section === section ? parsed.detail : null;
  };
  const [detail, setDetailState] = useState<string | null>(read);
  useEffect(() => {
    const onChange = () => setDetailState(read());
    window.addEventListener('hashchange', onChange);
    return () => window.removeEventListener('hashchange', onChange);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [section]);
  const setDetail = useCallback((next: DetailUpdate) => {
    const current = read();
    const value = typeof next === 'function' ? next(current) : next;
    if (value === current) return;
    window.location.hash = hashFor(section, value);
    setDetailState(value);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [section]);
  return [detail, setDetail];
}

/**
 * Clears a selection that was visible and then disappeared from the authorized list (for example
 * access was withdrawn), so restored visibility does not silently reopen it. A selection that has
 * not appeared yet (a shared link while the list is still loading) is left alone.
 */
export function useForgetVanishedSelection(selectedId: string | null, isPresent: boolean, clear: () => void) {
  const seen = useRef<string | null>(null);
  useEffect(() => {
    if (!selectedId) { seen.current = null; return; }
    if (isPresent) { seen.current = selectedId; return; }
    if (seen.current === selectedId) { seen.current = null; clear(); }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedId, isPresent]);
}
