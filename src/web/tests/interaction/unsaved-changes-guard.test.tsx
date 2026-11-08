import { act, cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { UnsavedChangesPrompt } from '../../src/components/UnsavedChangesPrompt';
import { hasUnsavedChanges, requestNavigation, useUnsavedChangesGuard } from '../../src/navigationGuard';
import { sectionFromHash } from '../../src/routing';

function Editor({ saved = 'Field simulator' }: { saved?: string }) {
  const [value, setValue] = useState(saved);
  const markSaved = useUnsavedChangesGuard(value !== saved);
  return <><label>Name<input value={value} onChange={event => setValue(event.target.value)} /></label>
    <button type="button" onClick={markSaved}>Save</button></>;
}
function Shell({ showEditor = true }: { showEditor?: boolean }) {
  const [section, setSection] = useState(sectionFromHash());
  // Same wiring as App: the router reacts to hashchange.
  useHashListener(() => setSection(sectionFromHash()));
  return <><UnsavedChangesPrompt /><p>Section: {section}</p>{showEditor && <Editor />}</>;
}
import { useEffect } from 'react';
function useHashListener(callback: () => void) {
  useEffect(() => { window.addEventListener('hashchange', callback); return () => window.removeEventListener('hashchange', callback); }, [callback]);
}
const changeHash = async (hash: string) => { await act(async () => { window.location.hash = hash; await new Promise(resolve => setTimeout(resolve, 20)); }); };

beforeEach(() => { window.location.hash = '#connections'; });
afterEach(() => { cleanup(); });

describe('unsaved-changes guard', () => {
  it('lets navigation through when nothing was edited, and asks nothing', async () => {
    render(<Shell />);
    await changeHash('#assets');
    expect(screen.getByText('Section: assets')).toBeTruthy();
    expect(screen.queryByRole('heading', { name: 'Leave without saving?' })).toBeNull();
  });

  it('asks before leaving, keeps the page and the typed text on Stay, and restores the address', async () => {
    const user = userEvent.setup(); render(<Shell />);
    await user.type(screen.getByLabelText('Name'), ' 2');
    await changeHash('#assets');
    expect(document.activeElement).toBe(screen.getByRole('heading', { name: 'Leave without saving?' }));
    expect(screen.getByText('Section: connections')).toBeTruthy();
    expect(window.location.hash).toBe('#connections');
    await user.click(screen.getByRole('button', { name: 'Stay and keep editing' }));
    expect(screen.queryByRole('heading', { name: 'Leave without saving?' })).toBeNull();
    expect((screen.getByLabelText('Name') as HTMLInputElement).value).toBe('Field simulator 2');
    expect(window.location.hash).toBe('#connections');
  });

  it('navigates to the address that was asked for when the person chooses Leave', async () => {
    const user = userEvent.setup(); render(<Shell />);
    await user.type(screen.getByLabelText('Name'), 'x');
    await changeHash('#assets');
    await user.click(screen.getByRole('button', { name: 'Leave without saving' }));
    await act(async () => { await new Promise(resolve => setTimeout(resolve, 20)); });
    expect(window.location.hash).toBe('#assets');
    expect(screen.getByText('Section: assets')).toBeTruthy();
  });

  it('is clean again when the edit is reverted', async () => {
    const user = userEvent.setup(); render(<Shell />);
    const name = screen.getByLabelText('Name');
    await user.type(name, 'x'); expect(hasUnsavedChanges()).toBe(true);
    await user.type(name, '{Backspace}'); expect(hasUnsavedChanges()).toBe(false);
    await changeHash('#assets');
    expect(screen.getByText('Section: assets')).toBeTruthy();
  });

  it('stops asking as soon as the form reports a successful save', async () => {
    const user = userEvent.setup(); render(<Shell />);
    await user.type(screen.getByLabelText('Name'), 'x');
    await user.click(screen.getByRole('button', { name: 'Save' }));
    expect(hasUnsavedChanges()).toBe(false);
    await changeHash('#assets');
    expect(screen.getByText('Section: assets')).toBeTruthy();
  });

  it('holds in-page navigation (requestNavigation) until Leave, and runs it at once when clean', async () => {
    const user = userEvent.setup(); let ran = 0;
    const { unmount } = render(<Shell />);
    act(() => requestNavigation(() => { ran += 1; }));
    expect(ran).toBe(1);
    await user.type(screen.getByLabelText('Name'), 'x');
    act(() => requestNavigation(() => { ran += 1; }));
    expect(ran).toBe(1);
    await user.click(screen.getByRole('button', { name: 'Leave without saving' }));
    expect(ran).toBe(2);
    unmount();
  });

  it('registers the browser unload prompt only while something is unsaved', async () => {
    const user = userEvent.setup(); render(<Shell />);
    const prompt = () => { const event = new Event('beforeunload', { cancelable: true }); window.dispatchEvent(event); return event.defaultPrevented; };
    expect(prompt()).toBe(false);
    await user.type(screen.getByLabelText('Name'), 'x');
    expect(prompt()).toBe(true);
    await user.type(screen.getByLabelText('Name'), '{Backspace}');
    expect(prompt()).toBe(false);
  });

  it('forgets a pending question when the form goes away', async () => {
    const user = userEvent.setup(); const { rerender } = render(<Shell />);
    await user.type(screen.getByLabelText('Name'), 'x');
    await changeHash('#assets');
    expect(screen.getByRole('heading', { name: 'Leave without saving?' })).toBeTruthy();
    rerender(<Shell showEditor={false} />);
    expect(screen.queryByRole('heading', { name: 'Leave without saving?' })).toBeNull();
  });
});
