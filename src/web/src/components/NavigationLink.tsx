export type Section = 'operations' | 'recommendations' | 'assets' | 'logic' | 'access';

export function NavigationLink({
  section,
  current,
  children,
}: {
  section: Section;
  current: Section;
  children: string;
}) {
  const shortLabel = { operations: 'OP', recommendations: 'RC', assets: 'AS', logic: 'LG', access: 'AC' }[section];

  return (
    <a
      aria-current={current === section ? 'page' : undefined}
      aria-label={children}
      className={section === current ? 'active' : undefined}
      href={'#' + section}
    >
      <span className="nav-label">{children}</span>
      <span className="nav-short" aria-hidden="true">{shortLabel}</span>
    </a>
  );
}
