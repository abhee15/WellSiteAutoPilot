import type { Section } from './components/NavigationLink';
import type { CurrentUserResponse } from './api/generated/v1/types.gen';
import { canOpenSection } from './security/sessionAccess';

/** Sections in landing priority order. */
export const SECTION_ORDER: readonly Section[] = ['operations', 'recommendations', 'assets', 'logic', 'access'];

export function sectionFromHash(): Section {
  const value = window.location.hash.replace('#', '').toLowerCase();
  return value === 'assets' || value === 'logic' || value === 'recommendations' || value === 'access'
    ? value : 'operations';
}

/** True when the address names a section; false for an empty or unknown hash (the default landing). */
export function hasExplicitSection(): boolean {
  const value = window.location.hash.replace('#', '').toLowerCase();
  return (SECTION_ORDER as readonly string[]).includes(value);
}

/** The first section this user may open, or null when they may open none. */
export function firstOpenableSection(user: CurrentUserResponse | null | undefined): Section | null {
  return SECTION_ORDER.find(section => canOpenSection(user, section)) ?? null;
}
