import type { Section } from './components/NavigationLink';

export function sectionFromHash(): Section {
  const value = window.location.hash.replace('#', '').toLowerCase();
  return value === 'assets' || value === 'logic' || value === 'recommendations' || value === 'access'
    ? value : 'operations';
}
