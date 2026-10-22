import { afterEach, describe, expect, it } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import { LoadingIndicator } from '../../src/components/LoadingIndicator';

afterEach(cleanup);

describe('LoadingIndicator', () => {
  it('announces its label politely and hides the spinner from assistive technology', () => {
    render(<LoadingIndicator label="Loading status…" />);
    const status = screen.getByRole('status');
    expect(status.getAttribute('aria-live')).toBe('polite');
    expect(status.textContent).toBe('Loading status…');
    expect(status.querySelector('.loading-spinner')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('can be small and inline for a line of text or a table cell', () => {
    render(<LoadingIndicator label="Loading…" size="small" inline />);
    const status = screen.getByRole('status');
    expect(status.getAttribute('data-size')).toBe('small');
    expect(status.hasAttribute('data-inline')).toBe(true);
  });
});
