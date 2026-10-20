import type { Section } from './NavigationLink';

/** Small line icons for the side panel. Decorative: the link's text already names the section. */
const PATHS: Record<Section, string> = {
  overview: 'M4 4h7v7H4zM13 4h7v4h-7zM13 10h7v10h-7zM4 13h7v7H4z',
  operations: 'M3 12h4l3-8 4 16 3-8h4',
  recommendations: 'M12 3a6 6 0 0 0-3.5 10.9V17h7v-3.1A6 6 0 0 0 12 3zM9.5 20.5h5',
  assets: 'M12 3v5M12 8H5v5M12 8h7v5M3 13h4v4H3zM17 13h4v4h-4zM10 17h4v4h-4z',
  logic: 'M6 4v10M6 14a3 3 0 1 0 0 6 3 3 0 0 0 0-6zM6 4a2 2 0 1 0 0 .01M18 4a2 2 0 1 0 0 .01M18 6v2a4 4 0 0 1-4 4h-4',
  connections: 'M9 7V3M15 7V3M7 7h10v4a5 5 0 0 1-10 0zM12 16v5',
  access: 'M12 3l7 3v5c0 4.5-3 8-7 10-4-2-7-5.5-7-10V6z',
};

export function NavIcon({ section }: { section: Section }) {
  return (
    <svg className="nav-icon" viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor"
      strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false">
      <path d={PATHS[section]} />
    </svg>
  );
}
