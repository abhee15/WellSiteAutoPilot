import { afterEach, describe, expect, it, vi } from 'vitest';
import { act, cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { CopyButton } from '../../src/views/CopyButton';
import { Status, StatusIndicator } from '../../src/components/Status';
import { ActionButton } from '../../src/components/ActionButton';
import { RefreshCw } from 'lucide-react';
import { FilterToolbar, FilterField } from '../../src/components/FilterToolbar';

afterEach(() => { cleanup(); vi.useRealTimers(); vi.restoreAllMocks(); });

describe('Status', () => {
  it('shows the word with its own shape, and the same tone always has the same shape', () => {
    const { container } = render(<><Status value="Failed" /><Status value="TimedOut" /><Status value="Completed" /></>);
    const shapes = [...container.querySelectorAll('.status-indicator')].map(node => ({
      tone: node.getAttribute('data-tone'), shape: node.querySelector('svg')?.getAttribute('class') ?? '',
    }));
    expect(shapes.map(item => item.tone)).toEqual(['error', 'error', 'success']);
    expect(shapes[0].shape).toBe(shapes[1].shape);
    expect(shapes[0].shape).not.toBe(shapes[2].shape);
    expect(screen.getByText('Timed out')).toBeTruthy();
  });

  it('keeps the icon out of the accessible name so a screen reader hears just the word', () => {
    render(<Status value="Healthy" />);
    const icon = document.querySelector('.status-indicator svg');
    expect(icon?.getAttribute('aria-hidden')).toBe('true');
    expect(screen.getByText('Healthy').closest('.status-indicator')?.textContent).toBe('Healthy');
  });

  it('lets a page word a state its own way without changing its tone', () => {
    render(<Status value="NeedsAttention" label="Check this" />);
    expect(screen.getByText('Check this').closest('.status-indicator')?.getAttribute('data-tone')).toBe('warning');
  });

  it('can be given a tone directly', () => {
    render(<StatusIndicator tone="info" label="Working" />);
    expect(screen.getByText('Working').closest('.status-indicator')?.getAttribute('data-tone')).toBe('info');
  });
});

describe('ActionButton (icon only)', () => {
  it('is named by its label, shows it as a hover tip, and calls back on click', async () => {
    const onClick = vi.fn();
    render(<ActionButton icon={RefreshCw} iconOnly label="Reload activity" onClick={onClick} />);
    const button = screen.getByRole('button', { name: 'Reload activity' });
    expect(button.getAttribute('title')).toBe('Reload activity');
    await userEvent.click(button);
    expect(onClick).toHaveBeenCalledTimes(1);
  });

  it('does not call back when disabled', async () => {
    const onClick = vi.fn();
    render(<ActionButton icon={RefreshCw} iconOnly label="Reload" onClick={onClick} disabled />);
    await userEvent.click(screen.getByRole('button', { name: 'Reload' }));
    expect(onClick).not.toHaveBeenCalled();
  });
});

describe('CopyButton', () => {
  it('is an icon button named for what it copies, with no visible text', () => {
    render(<CopyButton value="abc" label="Record ID" />);
    const button = screen.getByRole('button', { name: 'Copy Record ID' });
    expect(button.querySelector('.visually-hidden')?.textContent).toBe('Copy Record ID');
    expect(button.querySelector('svg')).toBeTruthy();
  });

  it('copies, then confirms with a check and a spoken status, then goes back to the copy icon', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    vi.stubGlobal('navigator', { clipboard: { writeText } });
    render(<CopyButton value="00000000-0000-4000-8000-000000000501" label="Record ID" />);
    const button = screen.getByRole('button', { name: 'Copy Record ID' });
    const before = button.querySelector('svg')?.getAttribute('class');
    vi.useFakeTimers({ shouldAdvanceTime: true });
    await act(async () => { button.click(); });
    expect(writeText).toHaveBeenCalledWith('00000000-0000-4000-8000-000000000501');
    expect(screen.getByRole('status').textContent).toBe('Copied');
    expect(button.getAttribute('data-state')).toBe('success');
    expect(button.getAttribute('title')).toBe('Copied');
    expect(button.querySelector('svg')?.getAttribute('class')).not.toBe(before);
    await act(async () => { vi.advanceTimersByTime(2100); });
    expect(screen.getByRole('status').textContent).toBe('');
    expect(button.getAttribute('data-state')).toBeNull();
    expect(button.querySelector('svg')?.getAttribute('class')).toBe(before);
    vi.unstubAllGlobals();
  });

  it('says so, and tells the person what to do, when the browser refuses to copy', async () => {
    vi.stubGlobal('navigator', { clipboard: { writeText: vi.fn().mockRejectedValue(new Error('denied')) } });
    Object.defineProperty(document, 'execCommand', { configurable: true, value: () => false });
    render(<CopyButton value="abc" label="Record ID" />);
    await userEvent.click(screen.getByRole('button', { name: 'Copy Record ID' }));
    expect((await screen.findByRole('status')).textContent).toContain('Could not copy');
    expect(screen.getByRole('button', { name: 'Copy Record ID' }).getAttribute('data-state')).toBe('error');
    vi.unstubAllGlobals();
  });
});

describe('FilterToolbar', () => {
  it('is one search box and a Filters button; the fields stay behind the button until it is opened', async () => {
    render(<FilterToolbar label="Role filters" search={{ value: '', label: 'Search', onChange: () => {} }}>
      <FilterField label="Type"><select aria-label="Type"><option>All</option></select></FilterField>
    </FilterToolbar>);
    expect(screen.getByRole('group', { name: 'Role filters' })).toBeTruthy();
    expect(screen.queryByRole('combobox', { name: 'Type' })).toBeNull();
    await userEvent.click(screen.getByRole('button', { name: /^Filters/ }));
    expect(screen.getByRole('combobox', { name: 'Type' })).toBeTruthy();
    screen.getByRole('combobox', { name: 'Type' }).focus();
    await userEvent.keyboard('{Escape}');
    expect(screen.queryByRole('combobox', { name: 'Type' })).toBeNull();
    expect(document.activeElement).toBe(screen.getByRole('button', { name: /^Filters/ }));
  });

  it('shows a removable chip per applied filter and clears everything from one button', async () => {
    const remove = vi.fn(); const clear = vi.fn();
    render(<FilterToolbar label="Filters" chips={[{ id: 'a', label: 'Awaiting', onRemove: remove }]}
      isFiltered onClear={clear} note="Showing 1 of 3"><span /></FilterToolbar>);
    expect(screen.getByText('Showing 1 of 3')).toBeTruthy();
    await userEvent.click(screen.getByRole('button', { name: 'Remove filter: Awaiting' }));
    expect(remove).toHaveBeenCalledTimes(1);
    await userEvent.click(screen.getByRole('button', { name: 'Clear filters' }));
    expect(clear).toHaveBeenCalledTimes(1);
  });

  it('applies on Enter when it has an onSubmit, and offers no Filters button when it has no fields', async () => {
    const onSubmit = vi.fn();
    render(<FilterToolbar label="Event filters" onSubmit={onSubmit}
      search={{ value: 'x', label: 'Action', onChange: () => {} }} />);
    expect(screen.queryByRole('button', { name: /^Filters/ })).toBeNull();
    await userEvent.type(screen.getByRole('searchbox', { name: 'Action' }), '{Enter}');
    expect(onSubmit).toHaveBeenCalledTimes(1);
  });
});
