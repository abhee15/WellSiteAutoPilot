import { afterEach, describe, expect, it, vi } from 'vitest';
import { act, cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { CopyButton } from '../../src/views/CopyButton';
import { Status, StatusIndicator } from '../../src/components/Status';
import { IconButton } from '../../src/components/IconButton';
import { RefreshCw } from 'lucide-react';

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

describe('IconButton', () => {
  it('is named by its label, shows it as a hover tip, and calls back on click', async () => {
    const onClick = vi.fn();
    render(<IconButton icon={RefreshCw} label="Reload activity" onClick={onClick} />);
    const button = screen.getByRole('button', { name: 'Reload activity' });
    expect(button.getAttribute('title')).toBe('Reload activity');
    await userEvent.click(button);
    expect(onClick).toHaveBeenCalledTimes(1);
  });

  it('does not call back when disabled', async () => {
    const onClick = vi.fn();
    render(<IconButton icon={RefreshCw} label="Reload" onClick={onClick} disabled />);
    await userEvent.click(screen.getByRole('button', { name: 'Reload' }));
    expect(onClick).not.toHaveBeenCalled();
  });
});

describe('CopyButton', () => {
  it('is an icon button named for what it copies, with no visible text', () => {
    render(<CopyButton value="abc" label="Record ID" />);
    const button = screen.getByRole('button', { name: 'Copy Record ID' });
    expect(button.textContent).toBe('');
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
