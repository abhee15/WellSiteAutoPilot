import type { ReactNode, Ref } from 'react';
import './tabStrip.css';

/** One appearance for page navigation and in-place tabpanels; semantics belong to their wrappers. */
export function TabStrip({ label, mode, children }: {
  label: string;
  mode: 'navigation' | 'tabs';
  children: ReactNode;
}) {
  return mode === 'navigation'
    ? <nav className="tab-strip" aria-label={label}>{children}</nav>
    : <div className="tab-strip" role="tablist" aria-label={label}>{children}</div>;
}

export function TabBar({ children, action, rootRef }: {
  children: ReactNode;
  action?: ReactNode;
  rootRef?: Ref<HTMLDivElement>;
}) {
  return <div className="tab-bar" ref={rootRef}>
    {children}
    {action && <div className="tab-bar-action">{action}</div>}
  </div>;
}
