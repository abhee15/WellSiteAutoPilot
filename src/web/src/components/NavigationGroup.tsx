import type { ReactNode } from 'react';
import './navigation.css';

/** A labelled cluster of navigation links; renders nothing when it has no visible links. */
export function NavigationGroup({ id, label, children }: { id: string; label: string; children: ReactNode[] }) {
  const visible = children.filter(Boolean);
  if (visible.length === 0) return null;
  return (
    <div className="nav-group" role="group" aria-labelledby={id}>
      <p className="nav-group-label" id={id}>{label}</p>
      {visible}
    </div>
  );
}
