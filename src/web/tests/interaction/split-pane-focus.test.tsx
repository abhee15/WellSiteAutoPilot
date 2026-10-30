import { useState } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useSplitPaneFocus } from '../../src/components/useSplitPaneFocus';

afterEach(() => { cleanup(); vi.restoreAllMocks(); });

function Harness({ revision = 0, removeOrigin = false }: { revision?: number; removeOrigin?: boolean }) {
  const [selected, select] = useState<string | null>(null);
  const focus = useSplitPaneFocus(selected);
  return <><button>Outside</button><div {...focus} data-selected={selected !== null}>
    <button onClick={() => select('new')}>Create</button>
    <div className="split-browse">{!removeOrigin && <button onClick={() => select('one')}>Open</button>}</div>
    <section className="split-selected" tabIndex={-1} aria-label="Details">
      {selected && <><button onClick={() => select(null)}>Back</button><input aria-label="Edit" /></>}
      <span>{revision}</span>
    </section>
  </div></>;
}

function geometry(narrow: boolean) {
  vi.spyOn(HTMLElement.prototype, 'getClientRects').mockImplementation(function (this: HTMLElement) {
    const hidden = this.classList.contains('split-browse') && narrow && this.parentElement?.dataset.selected === 'true';
    return (hidden ? [] : [{}]) as unknown as DOMRectList;
  });
}

describe('responsive split pane focus', () => {
  it('focuses details when they replace the list, then restores the opener', async () => {
    geometry(true); render(<Harness />);
    const opener = screen.getByRole('button', { name: 'Open' });
    opener.focus(); await userEvent.keyboard('{Enter}');
    expect(document.activeElement).toBe(screen.getByRole('region', { name: 'Details' }));
    await userEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(document.activeElement).toBe(opener);
  });

  it('keeps focus in the desktop list when both panes remain visible', async () => {
    geometry(false); render(<Harness />);
    const opener = screen.getByRole('button', { name: 'Open' });
    opener.focus(); await userEvent.keyboard('{Enter}');
    expect(document.activeElement).toBe(opener);
  });

  it('returns to a create action above the browse pane', async () => {
    geometry(true); render(<Harness />);
    const create = screen.getByRole('button', { name: 'Create' });
    await userEvent.click(create);
    expect(document.activeElement).toBe(screen.getByRole('region', { name: 'Details' }));
    await userEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(document.activeElement).toBe(create);
  });

  it('does not reset focus while the same selected record refreshes', async () => {
    geometry(true); const view = render(<Harness />);
    await userEvent.click(screen.getByRole('button', { name: 'Open' }));
    screen.getByRole('textbox', { name: 'Edit' }).focus();
    view.rerender(<Harness revision={1} />);
    expect(document.activeElement).toBe(screen.getByRole('textbox', { name: 'Edit' }));
  });

  it('does not restore focus to a record that was removed', async () => {
    geometry(true); const view = render(<Harness />);
    await userEvent.click(screen.getByRole('button', { name: 'Open' }));
    view.rerender(<Harness removeOrigin />);
    await userEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(document.activeElement).not.toBe(screen.getByRole('button', { name: 'Outside' }));
    expect(document.activeElement?.isConnected).toBe(true);
  });
});
