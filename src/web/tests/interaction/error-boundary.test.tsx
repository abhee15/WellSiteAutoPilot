import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ErrorBoundary } from '../../src/components/ErrorBoundary';
import { accessibilityViolations } from './axe';

let broken = true;
function Fragile() {
  if (broken) throw new Error('Rendering broke here');
  return <p>Recovered content</p>;
}

describe('ErrorBoundary', () => {
  beforeEach(() => {
    broken = true;
    // React and the boundary both report the error to the console; keep the test output readable.
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
  });
  afterEach(() => { cleanup(); vi.restoreAllMocks(); });

  it('shows a page-level message, moves focus to it and keeps working content out of the failed tree', async () => {
    render(<ErrorBoundary scope="page"><Fragile /></ErrorBoundary>);
    const heading = screen.getByRole('heading', { name: 'Something went wrong' });
    expect(heading.tagName).toBe('H2');
    expect(document.activeElement).toBe(heading);
    expect(screen.getByRole('alert').textContent).toMatch(/could not be displayed/);
    expect(screen.queryByText('Recovered content')).toBeNull();
    expect(console.error).toHaveBeenCalled();
  });

  it('lets the person try again, and shows the page once it renders', async () => {
    render(<ErrorBoundary scope="page"><Fragile /></ErrorBoundary>);
    broken = false;
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));
    expect(screen.getByText('Recovered content')).toBeTruthy();
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('starts fresh when the key changes, as it does when the person opens another section', () => {
    const { rerender } = render(<ErrorBoundary scope="page" key="a"><Fragile /></ErrorBoundary>);
    expect(screen.getByRole('alert')).toBeTruthy();
    broken = false;
    rerender(<ErrorBoundary scope="page" key="b"><Fragile /></ErrorBoundary>);
    expect(screen.getByText('Recovered content')).toBeTruthy();
  });

  it('offers a reload for an application-level failure, and shows only the message as detail', async () => {
    const reload = vi.fn();
    vi.stubGlobal('location', { ...window.location, reload });
    render(<ErrorBoundary scope="app"><Fragile /></ErrorBoundary>);
    expect(screen.getByRole('heading', { level: 1, name: 'Something went wrong' })).toBeTruthy();
    expect(screen.getByRole('main')).toBeTruthy();
    expect(screen.getByText('Rendering broke here')).toBeTruthy();
    await userEvent.click(screen.getByRole('button', { name: 'Reload page' }));
    expect(reload).toHaveBeenCalledOnce();
    vi.unstubAllGlobals();
  });

  it('has no accessibility violations in either scope', async () => {
    const page = render(<ErrorBoundary scope="page"><Fragile /></ErrorBoundary>);
    expect(await accessibilityViolations(page.container)).toEqual([]);
    cleanup();
    const app = render(<ErrorBoundary scope="app"><Fragile /></ErrorBoundary>);
    expect(await accessibilityViolations(app.container)).toEqual([]);
  });

  it('renders children untouched when nothing fails', () => {
    broken = false;
    render(<ErrorBoundary scope="page"><Fragile /></ErrorBoundary>);
    expect(screen.getByText('Recovered content')).toBeTruthy();
  });
});
