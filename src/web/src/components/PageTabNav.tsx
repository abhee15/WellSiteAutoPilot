import { useEffect, useRef } from 'react';
import type { ReactNode } from 'react';
import { TabBar, TabStrip } from './TabStrip';

export type PageTab<T extends string> = {
  id: T;
  label: string;
  href: string;
  count?: number | string;
};

/** Addressable page sections are links, not ARIA tabpanels. */
export function PageTabNav<T extends string>({ label, tabs, activeId, action, onNavigate }: {
  label: string;
  tabs: readonly PageTab<T>[];
  activeId: T;
  action?: ReactNode;
  /** Optional side-effect for regular in-page navigation; never replaces native link behavior. */
  onNavigate?: (id: T) => void;
}) {
  const rootRef = useRef<HTMLDivElement>(null);

  // Hash navigation can select a tab while the strip is scrolled elsewhere. Keep only
  // this strip in view without moving the page or its master/detail scroll panes.
  useEffect(() => {
    const strip = rootRef.current?.querySelector<HTMLElement>('.tab-strip');
    const active = strip?.querySelector<HTMLElement>('[aria-current="page"]');
    if (!strip || !active) return;
    const viewport = strip.getBoundingClientRect();
    const bounds = active.getBoundingClientRect();
    if (bounds.left < viewport.left) strip.scrollLeft -= viewport.left - bounds.left;
    else if (bounds.right > viewport.right) strip.scrollLeft += bounds.right - viewport.right;
  }, [activeId]);

  return <TabBar action={action} rootRef={rootRef}>
    <TabStrip label={label} mode="navigation">
      {tabs.map(tab => <a key={tab.id} className="tab-strip-item" href={tab.href}
        aria-label={tab.count === undefined ? tab.label : tab.label + ' ' + tab.count}
        aria-current={tab.id === activeId ? 'page' : undefined}
        onClick={event => {
          if (!event.defaultPrevented && event.button === 0 && !event.metaKey && !event.ctrlKey &&
            !event.shiftKey && !event.altKey) onNavigate?.(tab.id);
        }}>
        <span>{tab.label}</span>
        {tab.count !== undefined && <span className="tab-strip-count">{tab.count}</span>}
      </a>)}
    </TabStrip>
  </TabBar>;
}
