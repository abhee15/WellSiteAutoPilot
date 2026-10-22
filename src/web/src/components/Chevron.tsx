import { ChevronDown, ChevronRight, ChevronUp } from 'lucide-react';

/** The expand/collapse marker on a tree row: points down when open, right when closed. */
export function Disclosure({ open }: { open: boolean }) {
  const Icon = open ? ChevronDown : ChevronRight;
  return <Icon size={16} strokeWidth={2} aria-hidden="true" focusable="false" />;
}

/** A small down chevron, for "there is more below" and for dropdown buttons. */
export function ChevronMark({ direction = 'down' }: { direction?: 'down' | 'up' }) {
  const Icon = direction === 'down' ? ChevronDown : ChevronUp;
  return <Icon className="chevron-mark" size={16} strokeWidth={2} aria-hidden="true" focusable="false" />;
}
