import { createRef } from 'react';
import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { RefreshCw } from 'lucide-react';
import { SearchInput } from '../../src/components/SearchInput';
import { ActionButton } from '../../src/components/ActionButton';

afterEach(cleanup);
it('preserves native input labels, refs and caller-owned filtering', async () => {
  const ref = createRef<HTMLInputElement>(); const change = vi.fn(); const user = userEvent.setup();
  render(<label>Find wells<SearchInput ref={ref} defaultValue="" onChange={change} maxLength={8} /></label>);
  const input = screen.getByRole('searchbox', { name: 'Find wells' });
  expect(ref.current).toBe(input);
  await user.type(input, 'Alpha');
  expect(change).toHaveBeenCalledTimes(5);
  expect(screen.queryByRole('button')).toBeNull();
});
it.each([true, false])('submits exactly once from Enter or Space with iconOnly=%s', async iconOnly => {
  const submit = vi.fn(); const user = userEvent.setup();
  render(<form onSubmit={event => { event.preventDefault(); submit(); }}>
    <label htmlFor="query">Group name</label>
    <SearchInput id="query" defaultValue="Software" action={{ label: 'Search groups', type: 'submit', iconOnly }} />
  </form>);
  await user.click(screen.getByRole('searchbox')); await user.keyboard('{Enter}');
  expect(submit).toHaveBeenCalledTimes(1);
  const button = screen.getByRole('button', { name: 'Search groups' });
  button.focus(); await user.keyboard(' ');
  expect(submit).toHaveBeenCalledTimes(2);
});
it('keeps an embedded search from submitting the outer edit form', async () => {
  const save = vi.fn(); const search = vi.fn(); const user = userEvent.setup();
  render(<form onSubmit={event => { event.preventDefault(); save(); }}>
    <label htmlFor="source">Source query</label>
    <SearchInput id="source" defaultValue="Well" onKeyDown={event => {
      if (event.key === 'Enter') { event.preventDefault(); event.stopPropagation(); search(); }
    }} action={{ label: 'Search source', onClick: search }} />
  </form>);
  await user.click(screen.getByRole('searchbox')); await user.keyboard('{Enter}');
  await user.click(screen.getByRole('button', { name: 'Search source' }));
  expect(search).toHaveBeenCalledTimes(2); expect(save).not.toHaveBeenCalled();
});
it('keeps a stable name and prevents activation while busy or disabled', async () => {
  const action = vi.fn(); const user = userEvent.setup();
  const { rerender } = render(<ActionButton label="Refresh roles" icon={RefreshCw} iconOnly busy onClick={action} />);
  const button = screen.getByRole('button', { name: 'Refresh roles' }) as HTMLButtonElement;
  expect(button.disabled).toBe(true); expect(button.getAttribute('aria-busy')).toBe('true');
  await user.click(button); expect(action).not.toHaveBeenCalled();
  rerender(<ActionButton label="Refresh roles" icon={RefreshCw} iconOnly disabled onClick={action} />);
  expect(button.disabled).toBe(true);
});
