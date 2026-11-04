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

/** Reveal a horizontally focused/selected tab without scrolling the outer page or detail pane. */
export function keepTabVisible(strip: HTMLElement, item: HTMLElement) {
  const viewport = strip.getBoundingClientRect();
  const bounds = item.getBoundingClientRect();
  if (bounds.left < viewport.left) strip.scrollLeft -= viewport.left - bounds.left;
  else if (bounds.right > viewport.right) strip.scrollLeft += bounds.right - viewport.right;
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
