import type { KeyboardEvent } from 'react';
import './detailTabs.css';

export function DetailTabs<T extends string>({ prefix, label, tabs, selected, disabled = false, onSelect }: {
  prefix: string; label: string; tabs: readonly { id: T; label: string }[]; selected: T;
  disabled?: boolean; onSelect: (id: T) => void;
}) {
  function navigate(event: KeyboardEvent<HTMLButtonElement>, index: number) {
    if (disabled || !['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
    event.preventDefault();
    const next = event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1
      : (index + (event.key === 'ArrowRight' ? 1 : -1) + tabs.length) % tabs.length;
    onSelect(tabs[next]!.id);
    event.currentTarget.parentElement?.querySelectorAll<HTMLButtonElement>('[role="tab"]')[next]?.focus();
  }
  return <div className="detail-tabs" role="tablist" aria-label={label}>
    {tabs.map((tab, index) => <button className="detail-tab" key={tab.id} type="button" role="tab" id={`${prefix}-tab-${tab.id}`}
      aria-controls={`${prefix}-${tab.id}`} aria-selected={selected === tab.id} tabIndex={selected === tab.id ? 0 : -1}
      disabled={disabled} onClick={() => onSelect(tab.id)} onKeyDown={event => navigate(event, index)}>{tab.label}</button>)}
  </div>;
}
