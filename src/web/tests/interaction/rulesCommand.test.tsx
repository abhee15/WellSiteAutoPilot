import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, expect, test, vi } from 'vitest';
import { RulesEditor } from '../../src/views/AuthoringLogic';
import { blankRecommendation, emptyRules } from '../../src/views/rulesModel';

const rules = {
  ...emptyRules,
  rules: [
    {
      id: 'Slow',
      when: 'x > 1',
      reason: 'r',
      outcomeCode: 'SLOW',
      title: null,
      recommend: { ...blankRecommendation, valueExpression: '5' },
    },
  ],
};
afterEach(cleanup);

afterEach(cleanup);

const commands = [{ name: 'SET_SPEED', displayName: 'Set speed', quantity: 'speed', unit: 'spm' }];

test('choosing a command fixes the setpoint and unit to what the provider publishes', () => {
  const onChange = vi.fn();
  render(<RulesEditor rules={rules} inputNames={['x']} commands={commands} onChange={onChange} />);
  fireEvent.change(screen.getByLabelText('Command to send'), { target: { value: 'SET_SPEED' } });
  expect(onChange).toHaveBeenCalledOnce();
  const next = onChange.mock.calls[0]?.[0] as typeof rules;
  expect(next.rules[0]?.recommend).toMatchObject({ command: 'SET_SPEED', setpoint: 'speed', unit: 'spm' });
});

test('says so when the provider publishes no commands', () => {
  render(<RulesEditor rules={rules} inputNames={['x']} commands={[]} onChange={vi.fn()} />);
  expect(screen.queryByLabelText('Command to send')).toBeNull();
  expect(screen.getByText(/publishes no commands/)).toBeTruthy();
});
