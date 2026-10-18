import { NavIcon } from './NavIcon';

export type Section = 'overview' | 'operations' | 'recommendations' | 'assets' | 'logic' | 'access';

export function NavigationLink({
  section,
  current,
  children,
}: {
  section: Section;
  current: Section;
  children: string;
}) {
  return (
    <a
      aria-current={current === section ? 'page' : undefined}
      aria-label={children}
      className={section === current ? 'active' : undefined}
      href={'#' + section}
      title={children}
    >
      <NavIcon section={section} />
      <span className="nav-label">{children}</span>
    </a>
  );
}
