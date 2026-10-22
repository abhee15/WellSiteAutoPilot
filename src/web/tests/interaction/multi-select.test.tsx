import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { MultiSelect } from '../../src/components/MultiSelect';

const options = [
  { value: 'a', label: 'Alpha' }, { value: 'b', label: 'Beta' }, { value: 'c', label: 'Gamma' },
] as const;

function Host({ onChange }: { onChange?: (next: string[]) => void }) {
  const [selected, setSelected] = useState<string[]>([]);
  return <MultiSelect label="Status" allLabel="All statuses" options={options} selected={selected}
    onChange={next => { setSelected(next); onChange?.(next); }} />;
}

afterEach(cleanup);

describe('MultiSelect', () => {
  it('shows "all" until something is ticked, then the name or the count', async () => {
    const user = userEvent.setup();
    render(<Host />);
    expect(screen.getByText('All statuses')).toBeTruthy();
    await user.click(screen.getByText('All statuses'));
    await user.click(screen.getByLabelText('Beta'));
    expect(screen.getAllByText('Beta').length).toBeGreaterThan(0);
    await user.click(screen.getByLabelText('Gamma'));
    expect(screen.getByText('2 selected')).toBeTruthy();
  });

  it('keeps choices in option order and clears back to all', async () => {
    const user = userEvent.setup();
    const seen = vi.fn();
    render(<Host onChange={seen} />);
    await user.click(screen.getByText('All statuses'));
    await user.click(screen.getByLabelText('Gamma'));
    await user.click(screen.getByLabelText('Alpha'));
    expect(seen).toHaveBeenLastCalledWith(['a', 'c']);
    await user.click(screen.getByRole('button', { name: 'Clear selection' }));
    expect(seen).toHaveBeenLastCalledWith([]);
    expect(screen.getByText('All statuses')).toBeTruthy();
  });
});
