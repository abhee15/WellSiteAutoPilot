// Checks a rules expression as it is typed, with the same grammar and kind rules as the server's
// RuleExpressionParser and RuleEvaluator.Check, so the author sees the same refusal before saving.

export type ExpressionKind = 'number' | 'boolean' | 'text';

type ExpressionIssueCode =
  | 'char'
  | 'textOpen'
  | 'number'
  | 'ends'
  | 'unexpected'
  | 'bracket'
  | 'call'
  | 'chain'
  | 'unknownName'
  | 'unknownFunction'
  | 'arguments'
  | 'needs'
  | 'sameKind'
  | 'ifKind';

export interface ExpressionIssue {
  code: ExpressionIssueCode;
  position: number;
  /** The word, function, kind or count the message names. */
  detail: string;
}

type ExpressionCheck = { kind: ExpressionKind } | { issue: ExpressionIssue };

/** Minimum and maximum number of values each function takes; the server's table. */
const functionArity: Record<string, [number, number]> = {
  min: [2, 8],
  max: [2, 8],
  avg: [1, 8],
  abs: [1, 1],
  round: [1, 2],
  clamp: [3, 3],
  if: [3, 3],
};

interface Token {
  type: 'number' | 'text' | 'name' | 'symbol' | 'end';
  text: string;
  position: number;
}

class Failure extends Error {
  constructor(readonly issue: ExpressionIssue) {
    super(issue.code);
  }
}

function fail(code: ExpressionIssueCode, position: number, detail = ''): never {
  throw new Failure({ code, position, detail });
}

function tokenize(text: string): Token[] {
  const tokens: Token[] = [];
  let index = 0;
  while (index < text.length) {
    const c = text[index];
    if (/\s/.test(c)) {
      index++;
    } else if (/[0-9]/.test(c) || (c === '.' && /[0-9]/.test(text[index + 1] ?? ''))) {
      const start = index;
      while (index < text.length && /[0-9.]/.test(text[index])) index++;
      tokens.push({ type: 'number', text: text.slice(start, index), position: start });
    } else if (/[A-Za-z_]/.test(c)) {
      const start = index;
      while (index < text.length && /[A-Za-z0-9_]/.test(text[index])) index++;
      tokens.push({ type: 'name', text: text.slice(start, index), position: start });
    } else if (c === '"') {
      const close = text.indexOf('"', index + 1);
      if (close < 0) fail('textOpen', index);
      tokens.push({ type: 'text', text: text.slice(index + 1, close), position: index });
      index = close + 1;
    } else {
      const two = text.slice(index, index + 2);
      if (['<=', '>=', '==', '!=', '&&', '||'].includes(two)) {
        tokens.push({ type: 'symbol', text: two, position: index });
        index += 2;
      } else if ('+-*/<>()!,'.includes(c)) {
        tokens.push({ type: 'symbol', text: c, position: index });
        index++;
      } else {
        fail('char', index, c);
      }
    }
  }
  tokens.push({ type: 'end', text: '', position: text.length });
  return tokens;
}

const comparisons = ['<', '<=', '>', '>=', '==', '!='];

class Checker {
  private index = 0;

  constructor(
    private readonly tokens: Token[],
    private readonly known: Map<string, ExpressionKind>,
  ) {}

  private get current() {
    return this.tokens[this.index];
  }

  private isSymbol(symbol: string) {
    return this.current.type === 'symbol' && this.current.text === symbol;
  }

  private isWord(word: string) {
    return this.current.type === 'name' && this.current.text.toLowerCase() === word;
  }

  private isComparison() {
    return this.current.type === 'symbol' && comparisons.includes(this.current.text);
  }

  private require(actual: ExpressionKind, expected: ExpressionKind, what: string, position: number) {
    if (actual !== expected) fail('needs', position, what + '|' + expected);
  }

  finish(): ExpressionKind {
    const kind = this.or();
    if (this.current.type !== 'end') fail('unexpected', this.current.position, this.current.text);
    return kind;
  }

  private or(): ExpressionKind {
    let kind = this.and();
    while (this.isWord('or') || this.isSymbol('||')) {
      const at = this.current;
      this.index++;
      const right = this.and();
      this.require(kind, 'boolean', at.text, at.position);
      this.require(right, 'boolean', at.text, at.position);
      kind = 'boolean';
    }
    return kind;
  }

  private and(): ExpressionKind {
    let kind = this.not();
    while (this.isWord('and') || this.isSymbol('&&')) {
      const at = this.current;
      this.index++;
      const right = this.not();
      this.require(kind, 'boolean', at.text, at.position);
      this.require(right, 'boolean', at.text, at.position);
      kind = 'boolean';
    }
    return kind;
  }

  private not(): ExpressionKind {
    if (this.isWord('not') || this.isSymbol('!')) {
      const at = this.current;
      this.index++;
      const operand = this.not();
      this.require(operand, 'boolean', 'not', at.position);
      return operand;
    }
    return this.comparison();
  }

  private comparison(): ExpressionKind {
    const left = this.additive();
    if (!this.isComparison()) return left;
    const at = this.current;
    this.index++;
    const right = this.additive();
    if (this.isComparison()) fail('chain', this.current.position);
    if (at.text === '==' || at.text === '!=') {
      if (left !== right) fail('sameKind', at.position);
    } else {
      this.require(left, 'number', at.text, at.position);
      this.require(right, 'number', at.text, at.position);
    }
    return 'boolean';
  }

  private additive(): ExpressionKind {
    let kind = this.multiplicative();
    while (this.isSymbol('+') || this.isSymbol('-')) {
      const at = this.current;
      this.index++;
      const right = this.multiplicative();
      this.require(kind, 'number', at.text, at.position);
      this.require(right, 'number', at.text, at.position);
      kind = 'number';
    }
    return kind;
  }

  private multiplicative(): ExpressionKind {
    let kind = this.unary();
    while (this.isSymbol('*') || this.isSymbol('/')) {
      const at = this.current;
      this.index++;
      const right = this.unary();
      this.require(kind, 'number', at.text, at.position);
      this.require(right, 'number', at.text, at.position);
      kind = 'number';
    }
    return kind;
  }

  private unary(): ExpressionKind {
    if (this.isSymbol('-')) {
      const at = this.current;
      this.index++;
      const operand = this.unary();
      this.require(operand, 'number', '-', at.position);
      return operand;
    }
    return this.primary();
  }

  private primary(): ExpressionKind {
    const token = this.current;
    switch (token.type) {
      case 'number':
        this.index++;
        if (!/^(\d+\.?\d*|\.\d+)$/.test(token.text)) fail('number', token.position, token.text);
        return 'number';
      case 'text':
        this.index++;
        return 'text';
      case 'name': {
        this.index++;
        const word = token.text.toLowerCase();
        if (word === 'true' || word === 'false') return 'boolean';
        if (this.isSymbol('(')) return this.call(token);
        const kind = this.known.get(token.text);
        if (kind === undefined) fail('unknownName', token.position, token.text);
        return kind;
      }
      case 'symbol':
        if (token.text === '(') {
          this.index++;
          const inner = this.or();
          if (!this.isSymbol(')')) fail('bracket', this.current.position);
          this.index++;
          return inner;
        }
        return fail('unexpected', token.position, token.text);
      default:
        return fail('ends', token.position);
    }
  }

  private call(name: Token): ExpressionKind {
    this.index++;
    const kinds: ExpressionKind[] = [];
    if (!this.isSymbol(')')) {
      kinds.push(this.or());
      while (this.isSymbol(',')) {
        this.index++;
        kinds.push(this.or());
      }
    }
    if (!this.isSymbol(')')) fail('call', this.current.position);
    this.index++;
    const fn = name.text.toLowerCase();
    const arity = Object.hasOwn(functionArity, fn) ? functionArity[fn] : undefined;
    if (!arity) return fail('unknownFunction', name.position, fn);
    if (kinds.length < arity[0] || kinds.length > arity[1])
      fail(
        'arguments',
        name.position,
        fn + '|' + (arity[0] === arity[1] ? String(arity[0]) : arity[0] + ' to ' + arity[1]),
      );
    if (fn === 'if') {
      this.require(kinds[0], 'boolean', 'if', name.position);
      if (kinds[1] !== kinds[2]) fail('ifKind', name.position);
      return kinds[1];
    }
    for (const kind of kinds) this.require(kind, 'number', fn, name.position);
    return 'number';
  }
}

/** The kind an expression produces, or what is wrong and where. Empty text is not an error here. */
export function checkExpression(text: string, names: readonly string[]): ExpressionCheck | null {
  if (text.trim() === '') return null;
  const known = new Map<string, ExpressionKind>();
  for (const name of names) known.set(name, 'number');
  try {
    return { kind: new Checker(tokenize(text), known).finish() };
  } catch (error) {
    if (error instanceof Failure) return { issue: error.issue };
    throw error;
  }
}
