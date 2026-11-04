import { useState } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { PageTabNav } from '../../src/components/PageTabNav';
import { SectionTabs } from '../../src/components/SectionTabs';
import { accessibilityViolations } from './axe';

afterEach(() => { cleanup(); window.location.hash = ''; vi.restoreAllMocks(); });

const pageTabs = [
  { id: 'configured', label: 'Configured logic', href: '#logic', count: 12 },
  { id: 'schedules', label: 'Schedules', href: '#logic/schedules' },
  { id: 'modules', label: 'Modules', href: '#logic/modules', count: 4 },
] as const;

describe('PageTabNav', () => {
  it('uses native navigation links and keeps the trailing action separate from the scroll strip', async () => {
    const create = vi.fn();
    const view = render(<PageTabNav label="Logic sections" tabs={pageTabs} activeId="configured"
      action={<button type="button" onClick={create}>Create logic</button>} />);
    const nav = screen.getByRole('navigation', { name: 'Logic sections' });
    const links = within(nav).getAllByRole('link');
    expect(links.map(link => link.getAttribute('href'))).toEqual(['#logic', '#logic/schedules', '#logic/modules']);
    expect(links[0].getAttribute('aria-current')).toBe('page');
    expect(links[1].hasAttribute('aria-current')).toBe(false);
    expect(within(nav).queryByRole('tab')).toBeNull();
    expect(within(nav).queryByRole('button')).toBeNull();
    const action = screen.getByRole('button', { name: 'Create logic' });
    expect(action.closest('.tab-bar-action')).not.toBeNull();
    expect(action.closest('.tab-strip')).toBeNull();
    await userEvent.click(action);
    expect(create).toHaveBeenCalledTimes(1);
    expect(await accessibilityViolations(view.container)).toEqual([]);
  });

  it('runs route-specific cleanup without intercepting the native link', async () => {
    const onNavigate = vi.fn();
    const user = userEvent.setup();
    render(<PageTabNav label="Administration" tabs={[
      { id: 'users', label: 'User assignments', href: '#access' },
      { id: 'grants', label: 'Scoped grants', href: '#access/grants' },
    ]} activeId="users" onNavigate={onNavigate} />);
    await user.click(screen.getByRole('link', { name: 'Scoped grants' }));
    expect(onNavigate).toHaveBeenCalledExactlyOnceWith('grants');
    expect(window.location.hash).toBe('#access/grants');
  });

  it('does not swallow ordinary hash-link navigation or keyboard Tab focus', async () => {
    const user = userEvent.setup();
    render(<PageTabNav label="Logic sections" tabs={pageTabs} activeId="configured" />);
    const nav = screen.getByRole('navigation', { name: 'Logic sections' });
    await user.tab();
    expect(document.activeElement).toBe(within(nav).getByRole('link', { name: 'Configured logic 12' }));
    await user.tab();
    expect(document.activeElement).toBe(within(nav).getByRole('link', { name: 'Schedules' }));
    await user.click(within(nav).getByRole('link', { name: 'Modules 4' }));
    expect(window.location.hash).toBe('#logic/modules');
  });

  it('keeps an activated link visible without scrolling the enclosing page', () => {
    const view = render(<PageTabNav label="Logic sections" tabs={pageTabs} activeId="configured" />);
    const strip = view.container.querySelector<HTMLElement>('.tab-strip')!;
    const modules = screen.getByRole('link', { name: 'Modules 4' });
    vi.spyOn(strip, 'getBoundingClientRect').mockReturnValue({ left: 0, right: 120 } as DOMRect);
    vi.spyOn(modules, 'getBoundingClientRect').mockReturnValue({ left: 160, right: 240 } as DOMRect);
    view.rerender(<PageTabNav label="Logic sections" tabs={pageTabs} activeId="modules" />);
    expect(strip.scrollLeft).toBe(120);
    expect(modules.getAttribute('aria-current')).toBe('page');
  });
});

const sectionTabs = [
  { id: 'overview', label: 'Overview' },
  { id: 'configuration', label: 'Configuration' },
  { id: 'activity', label: 'Activity' },
] as const;
type SectionId = typeof sectionTabs[number]['id'];

function SectionFixture({ locked = false, withoutActivity = false, onSelect }: {
  locked?: boolean;
  withoutActivity?: boolean;
  onSelect?: (id: SectionId) => void;
}) {
  const [selected, setSelected] = useState<SectionId>('overview');
  const tabs = withoutActivity ? sectionTabs.filter(tab => tab.id !== 'activity') : sectionTabs;
  return <>
    <SectionTabs prefix="fixture" label="Logic detail sections" tabs={tabs} selected={selected}
      disabled={locked} onSelect={next => { setSelected(next); onSelect?.(next); }} />
    {tabs.map(tab => <div role="tabpanel" key={tab.id} id={'fixture-' + tab.id}
      aria-labelledby={'fixture-tab-' + tab.id} hidden={selected !== tab.id}>{tab.label} content</div>)}
  </>;
}

describe('SectionTabs', () => {
  it('moves keyboard focus without activation and activates with Enter or Space', async () => {
    const onSelect = vi.fn();
    const user = userEvent.setup();
    const view = render(<SectionFixture onSelect={onSelect} />);
    const overview = screen.getByRole('tab', { name: 'Overview' });
    const configuration = screen.getByRole('tab', { name: 'Configuration' });
    overview.focus();
    await user.keyboard('{ArrowRight}');
    expect(document.activeElement).toBe(configuration);
    expect(configuration.tabIndex).toBe(0);
    expect(overview.tabIndex).toBe(-1);
    expect(overview.getAttribute('aria-selected')).toBe('true');
    expect(onSelect).not.toHaveBeenCalled();
    await user.keyboard('{Enter}');
    expect(configuration.getAttribute('aria-selected')).toBe('true');
    expect(onSelect).toHaveBeenCalledTimes(1);
    expect(onSelect).toHaveBeenCalledWith('configuration');
    await user.keyboard('{End}');
    const activity = screen.getByRole('tab', { name: 'Activity' });
    expect(document.activeElement).toBe(activity);
    await user.keyboard(' ');
    expect(activity.getAttribute('aria-selected')).toBe('true');
    expect(onSelect).toHaveBeenCalledWith('activity');
    await user.keyboard('{Home}');
    expect(document.activeElement).toBe(overview);
    expect(activity.getAttribute('aria-selected')).toBe('true');
    expect(overview.getAttribute('aria-controls')).toBe('fixture-overview');
    expect(screen.getByRole('tabpanel', { name: 'Activity' }).getAttribute('aria-labelledby')).toBe('fixture-tab-activity');
    expect(await accessibilityViolations(view.container)).toEqual([]);
  });

  it('honors editor locks, and omits inaccessible tabs without losing existing panel IDs', async () => {
    const user = userEvent.setup();
    const onSelect = vi.fn();
    const view = render(<SectionFixture locked onSelect={onSelect} />);
    const configuration = screen.getByRole('tab', { name: 'Configuration' });
    expect(configuration.hasAttribute('disabled')).toBe(true);
    await user.click(configuration);
    expect(onSelect).not.toHaveBeenCalled();
    view.rerender(<SectionFixture withoutActivity onSelect={onSelect} />);
    expect(screen.queryByRole('tab', { name: 'Activity' })).toBeNull();
    expect(screen.getByRole('tablist', { name: 'Logic detail sections' })).toBeTruthy();
    expect(screen.getByRole('tab', { name: 'Overview' }).getAttribute('aria-controls')).toBe('fixture-overview');
    expect(screen.getByRole('tab', { name: 'Overview' }).hasAttribute('disabled')).toBe(false);
  });

  it('skips individually disabled tabs during manual keyboard navigation', async () => {
    const user = userEvent.setup();
    const tabs = [
      { id: 'first', label: 'First' },
      { id: 'middle', label: 'Middle', disabled: true },
      { id: 'last', label: 'Last' },
    ] as const;
    const onSelect = vi.fn();
    render(<SectionTabs prefix="skipping" label="Sections" tabs={tabs} selected="first" onSelect={onSelect} />);
    screen.getByRole('tab', { name: 'First' }).focus();
    await user.keyboard('{ArrowRight}');
    expect(document.activeElement).toBe(screen.getByRole('tab', { name: 'Last' }));
    expect(onSelect).not.toHaveBeenCalled();
  });
});
