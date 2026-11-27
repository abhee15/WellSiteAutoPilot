import { describe, expect, it } from 'vitest';
import { parseScenarios } from '../../src/views/AuthoringTests';
import { blankRecommendation, logicIncomplete, parseRules } from '../../src/views/rulesModel';

const rules = (recommend: object) =>
  parseRules(
    JSON.stringify({
      parameters: [{ id: 'MaxSpeed', default: 8 }],
      calculations: [],
      rules: [{ id: 'r', when: 'true', reason: 'x', outcomeCode: 'X', recommend }],
    }),
  );

describe('setpoint limits from settings', () => {
  it('reads limits named after a setting', () => {
    const item = rules({ setpoint: 'S', valueExpression: '1', maximumFrom: 'MaxSpeed' }).rules[0].recommend;
    expect(item?.maximumFrom).toBe('MaxSpeed');
    expect(item?.minimumFrom).toBeNull();
  });

  it('accepts a named largest change only with a current value', () => {
    const base = { ...blankRecommendation, setpoint: 'S', valueExpression: '1', maximumChangeFrom: 'MaxSpeed' };
    expect(logicIncomplete(rules(base), [])).toBe(true);
    expect(logicIncomplete(rules({ ...base, currentValue: 'MaxSpeed' }), [])).toBe(false);
  });
});

describe('saved scenarios', () => {
  it('reads a saved list and ignores anything else', () => {
    const saved = [{ name: 'Scenario 1', values: [], settings: [], expectedOutcome: 'NONE' }];
    expect(parseScenarios(JSON.stringify(saved))).toEqual(saved);
    expect(parseScenarios('{}')).toEqual([]);
    expect(parseScenarios('nope')).toEqual([]);
    expect(parseScenarios(null)).toEqual([]);
  });
});

describe('commands on a rule', () => {
  it('keeps the command a rule sends when it is read and saved again', () => {
    const json = JSON.stringify({
      parameters: [],
      calculations: [],
      rules: [{ id: 'R', when: 'x > 1', reason: 'r', outcomeCode: 'O', title: null, recommend: { ...blankRecommendation, setpoint: 'speed', valueExpression: '5', unit: 'spm', command: 'SET_SPEED' } }],
    });
    expect(parseRules(json).rules[0]?.recommend?.command).toBe('SET_SPEED');
  });
});
