import { describe, expect, it } from 'vitest';
import { checkExpression } from '../../src/views/expressionCheck';

const names = ['SPM', 'Fillage'];
const code = (text: string) => {
  const result = checkExpression(text, names);
  return 'issue' in result ? result.issue.code : result.kind;
};

describe('expression check', () => {
  it('gives the kind of a valid expression', () => {
    expect(code('SPM * 2 + 1')).toBe('number');
    expect(code('Fillage < 50 and SPM >= 3')).toBe('boolean');
    expect(code('if(SPM > 3, 1, 2)')).toBe('number');
    expect(code('clamp(SPM, 1, 10)')).toBe('number');
  });

  it('refuses unknown names and functions', () => {
    expect(code('Speed + 1')).toBe('unknownName');
    expect(code('foo(1)')).toBe('unknownFunction');
  });

  it('refuses the wrong number of arguments', () => {
    expect(code('abs(1, 2)')).toBe('arguments');
    expect(code('clamp(1, 2)')).toBe('arguments');
  });

  it('refuses chained comparisons and mixed kinds', () => {
    expect(code('1 < SPM < 5')).toBe('chain');
    expect(code('SPM == (SPM > 1)')).toBe('sameKind');
  });

  it('refuses unclosed brackets and text, and reports the position', () => {
    expect(code('(SPM + 1')).not.toBe('number');
    const result = checkExpression('SPM + Speed', names);
    expect('issue' in result && result.issue.position).toBe(6);
  });
});
