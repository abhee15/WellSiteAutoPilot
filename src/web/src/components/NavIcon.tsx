import {
  Activity, GitBranch, LayoutDashboard, Lightbulb, Network, Plug, Server, ShieldCheck,
} from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import type { Section } from './NavigationLink';

/** Icons for the side panel (Lucide, ISC licence). Decorative: the link's text already names the section. */
const ICONS: Record<Section, LucideIcon> = {
  overview: LayoutDashboard,
  operations: Activity,
  recommendations: Lightbulb,
  assets: Network,
  logic: GitBranch,
  connections: Plug,
  status: Server,
  access: ShieldCheck,
};

export function NavIcon({ section }: { section: Section }) {
  const Icon = ICONS[section];
  return <Icon className="nav-icon" size={20} strokeWidth={1.75} aria-hidden="true" focusable="false" />;
}
