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
export function PageTabNav<T extends string>({ label, tabs, activeId, action }: {
  label: string;
  tabs: readonly PageTab<T>[];
  activeId: T;
  action?: ReactNode;
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
        aria-current={tab.id === activeId ? 'page' : undefined}>
        <span>{tab.label}</span>
        {tab.count !== undefined && <span className="tab-strip-count">{tab.count}</span>}
      </a>)}
    </TabStrip>
  </TabBar>;
}
