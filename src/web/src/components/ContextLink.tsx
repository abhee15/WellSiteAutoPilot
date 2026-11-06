import type { ComponentProps } from 'react';
import { ChevronRight } from 'lucide-react';
import './contextLink.css';

/** A lightweight standalone destination in a row/card; workflow actions use ActionLink. */
export function ContextLink({ label, className = '', ...props }: Omit<ComponentProps<'a'>, 'children' | 'href'> & {
  label: string; href: string;
}) {
  return <a {...props} className={['action-link', className].filter(Boolean).join(' ')}>
    <span>{label}</span><ChevronRight size={16} aria-hidden="true" />
  </a>;
}
