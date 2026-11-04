import { useEffect, useState } from 'react';
import type { KeyboardEvent } from 'react';
import { TabBar, TabStrip } from './TabStrip';

export type SectionTab<T extends string> = { id: T; label: string; disabled?: boolean };

/** In-place tabpanels: arrow keys move focus; Enter/Space activates the focused tab. */
export function SectionTabs<T extends string>({ prefix, label, tabs, selected, disabled = false, onSelect }: {
  prefix: string;
  label: string;
  tabs: readonly SectionTab<T>[];
  selected: T;
  disabled?: boolean;
  onSelect: (id: T) => void;
}) {
  const [focusedId, setFocusedId] = useState<T>(selected);
  useEffect(() => { setFocusedId(selected); }, [selected]);

  const focusableId = tabs.some(tab => tab.id === focusedId && !tab.disabled) ? focusedId : selected;
  function navigate(event: KeyboardEvent<HTMLButtonElement>, id: T) {
    if (disabled || !['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
    const available = tabs.filter(tab => !tab.disabled);
    if (!available.length) return;
    event.preventDefault();
    const index = available.findIndex(tab => tab.id === id);
    const nextIndex = event.key === 'Home' ? 0
      : event.key === 'End' ? available.length - 1
        : (index + (event.key === 'ArrowRight' ? 1 : -1) + available.length) % available.length;
    const next = available[nextIndex]!;
    const buttons = event.currentTarget.parentElement?.querySelectorAll<HTMLButtonElement>('[role="tab"]');
    const target = Array.from(buttons ?? []).find(button => button.id === prefix + '-tab-' + next.id);
    target?.focus();
    setFocusedId(next.id);
  }

  return <TabBar>
    <TabStrip label={label} mode="tabs">
      {tabs.map(tab => <button key={tab.id} type="button" role="tab" className="tab-strip-item"
        id={prefix + '-tab-' + tab.id} aria-controls={prefix + '-' + tab.id}
        aria-selected={selected === tab.id} tabIndex={focusableId === tab.id && !tab.disabled ? 0 : -1}
        disabled={disabled || tab.disabled}
        onFocus={() => setFocusedId(tab.id)}
        onClick={() => { setFocusedId(tab.id); onSelect(tab.id); }}
        onKeyDown={event => navigate(event, tab.id)}>{tab.label}</button>)}
    </TabStrip>
  </TabBar>;
}
