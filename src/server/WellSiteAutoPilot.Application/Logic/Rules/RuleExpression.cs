using System.Globalization;

namespace WellSiteAutoPilot.Application.Logic.Rules;

public enum RuleValueKind
{
    Number,
    Boolean,
    Text
}

/// <summary>A value an expression can produce: a decimal number, a true/false, or a piece of text.</summary>
public readonly record struct RuleValue
{
    private RuleValue(RuleValueKind kind, decimal number, bool boolean, string? text)
    {
        Kind = kind;
        Number = number;
        Boolean = boolean;
        Text = text ?? string.Empty;
    }

    public RuleValueKind Kind { get; }
    public decimal Number { get; }
    public bool Boolean { get; }
    public string Text { get; }

    public static RuleValue OfNumber(decimal value) => new(RuleValueKind.Number, value, false, null);
    public static RuleValue OfBoolean(bool value) => new(RuleValueKind.Boolean, 0m, value, null);
    public static RuleValue OfText(string value) => new(RuleValueKind.Text, 0m, false, value);

    public override string ToString() => Kind switch
    {
        RuleValueKind.Number => Number.ToString(CultureInfo.InvariantCulture),
        RuleValueKind.Boolean => Boolean ? "true" : "false",
        _ => Text
    };
}

/// <summary>An authoring or evaluation failure with a stable code, and the position in the text when there is one.</summary>
public sealed class RuleExpressionException(string code, string message, int position = -1) : Exception(message)
{
    public string Code { get; } = code;
    public int Position { get; } = position;
}

public abstract record RuleNode;

public sealed record RuleLiteral(RuleValue Value) : RuleNode;

public sealed record RuleReference(string Name, int Position) : RuleNode;

public sealed record RuleUnary(string Operator, RuleNode Operand) : RuleNode;

public sealed record RuleBinary(string Operator, RuleNode Left, RuleNode Right) : RuleNode;

public sealed record RuleCall(string Name, IReadOnlyList<RuleNode> Arguments, int Position) : RuleNode;

/// <summary>
/// The expression language of a rules module. Deliberately small: numbers, true/false and text; arithmetic,
/// comparison and logic; a fixed set of functions. There is no loop, no assignment and no way to reach anything
/// outside the values the module declares, so an expression is always safe to evaluate and always ends.
/// </summary>
public static class RuleExpressionParser
{
    public const int MaximumLength = 512;
    public const int MaximumNodes = 128;
    public const int MaximumDepth = 32;

    public static RuleNode Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new RuleExpressionException("RULE_EXPRESSION_EMPTY", "The expression is empty.");
        if (text.Length > MaximumLength)
            throw new RuleExpressionException("RULE_EXPRESSION_TOO_LONG", "The expression is too long.");

        var parser = new Parser(Tokenize(text));
        var node = parser.ParseOr(0);
        parser.ExpectEnd();
        return node;
    }

    /// <summary>The names an expression reads, in the order they first appear.</summary>
    public static IReadOnlyList<string> References(RuleNode node)
    {
        var names = new List<string>();
        Collect(node, names);
        return names;
    }

    private static void Collect(RuleNode node, List<string> names)
    {
        switch (node)
        {
            case RuleReference reference:
                if (!names.Contains(reference.Name, StringComparer.Ordinal)) names.Add(reference.Name);
                break;
            case RuleUnary unary:
                Collect(unary.Operand, names);
                break;
            case RuleBinary binary:
                Collect(binary.Left, names);
                Collect(binary.Right, names);
                break;
            case RuleCall call:
                foreach (var argument in call.Arguments) Collect(argument, names);
                break;
        }
    }

    private enum TokenKind
    {
        Number,
        Text,
        Name,
        Symbol,
        End
    }

    private readonly record struct Token(TokenKind Kind, string Text, int Position);

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var index = 0;
        while (index < text.Length)
        {
            var c = text[index];
            if (char.IsWhiteSpace(c))
            {
                index++;
            }
            else if (char.IsAsciiDigit(c) || (c == '.' && index + 1 < text.Length && char.IsAsciiDigit(text[index + 1])))
            {
                var start = index;
                while (index < text.Length && (char.IsAsciiDigit(text[index]) || text[index] == '.')) index++;
                tokens.Add(new(TokenKind.Number, text[start..index], start));
            }
            else if (char.IsAsciiLetter(c) || c == '_')
            {
                var start = index;
                while (index < text.Length && (char.IsAsciiLetterOrDigit(text[index]) || text[index] == '_')) index++;
                tokens.Add(new(TokenKind.Name, text[start..index], start));
            }
            else if (c == '"')
            {
                var start = index;
                index++;
                var close = text.IndexOf('"', index);
                if (close < 0)
                    throw new RuleExpressionException("RULE_EXPRESSION_SYNTAX", "A text value is not closed.", start);
                tokens.Add(new(TokenKind.Text, text[index..close], start));
                index = close + 1;
            }
            else
            {
                var two = index + 1 < text.Length ? text.Substring(index, 2) : string.Empty;
                if (two is "<=" or ">=" or "==" or "!=" or "&&" or "||")
                {
                    tokens.Add(new(TokenKind.Symbol, two, index));
                    index += 2;
                }
                else if ("+-*/<>()!,".Contains(c, StringComparison.Ordinal))
                {
                    tokens.Add(new(TokenKind.Symbol, c.ToString(), index));
                    index++;
                }
                else
                {
                    throw new RuleExpressionException("RULE_EXPRESSION_SYNTAX", $"Unexpected character '{c}'.", index);
                }
            }
        }

        tokens.Add(new(TokenKind.End, string.Empty, text.Length));
        return tokens;
    }

    private sealed class Parser(List<Token> tokens)
    {
        private int _index;
        private int _nodes;

        private Token Current => tokens[_index];

        public void ExpectEnd()
        {
            if (Current.Kind != TokenKind.End)
                throw new RuleExpressionException("RULE_EXPRESSION_SYNTAX", $"Unexpected '{Current.Text}'.", Current.Position);
        }

        private T Count<T>(T node) where T : RuleNode
        {
            if (++_nodes > MaximumNodes)
                throw new RuleExpressionException("RULE_EXPRESSION_TOO_COMPLEX", "The expression is too complex.");
            return node;
        }

        private bool IsSymbol(string symbol) => Current.Kind == TokenKind.Symbol && Current.Text == symbol;

        private bool IsWord(string word) =>
            Current.Kind == TokenKind.Name && string.Equals(Current.Text, word, StringComparison.OrdinalIgnoreCase);

        private static void CheckDepth(int depth)
        {
            if (depth > MaximumDepth)
                throw new RuleExpressionException("RULE_EXPRESSION_TOO_COMPLEX", "The expression is nested too deeply.");
        }

        public RuleNode ParseOr(int depth)
        {
            CheckDepth(depth);
            var left = ParseAnd(depth + 1);
            while (IsWord("or") || IsSymbol("||"))
            {
                _index++;
                left = Count(new RuleBinary("or", left, ParseAnd(depth + 1)));
            }

            return left;
        }

        private RuleNode ParseAnd(int depth)
        {
            CheckDepth(depth);
            var left = ParseNot(depth + 1);
            while (IsWord("and") || IsSymbol("&&"))
            {
                _index++;
                left = Count(new RuleBinary("and", left, ParseNot(depth + 1)));
            }

            return left;
        }

        private RuleNode ParseNot(int depth)
        {
            CheckDepth(depth);
            if (IsWord("not") || IsSymbol("!"))
            {
                _index++;
                return Count(new RuleUnary("not", ParseNot(depth + 1)));
            }

            return ParseComparison(depth + 1);
        }

        private RuleNode ParseComparison(int depth)
        {
            CheckDepth(depth);
            var left = ParseAdditive(depth + 1);
            if (Current.Kind == TokenKind.Symbol && Current.Text is "<" or "<=" or ">" or ">=" or "==" or "!=")
            {
                var op = Current.Text;
                _index++;
                var right = ParseAdditive(depth + 1);
                if (Current.Kind == TokenKind.Symbol && Current.Text is "<" or "<=" or ">" or ">=" or "==" or "!=")
                    throw new RuleExpressionException("RULE_EXPRESSION_SYNTAX",
                        "Compare two values at a time; combine comparisons with and / or.", Current.Position);
                return Count(new RuleBinary(op, left, right));
            }

            return left;
        }

        private RuleNode ParseAdditive(int depth)
        {
            CheckDepth(depth);
            var left = ParseMultiplicative(depth + 1);
            while (IsSymbol("+") || IsSymbol("-"))
            {
                var op = Current.Text;
                _index++;
                left = Count(new RuleBinary(op, left, ParseMultiplicative(depth + 1)));
            }

            return left;
        }

        private RuleNode ParseMultiplicative(int depth)
        {
            CheckDepth(depth);
            var left = ParseUnary(depth + 1);
            while (IsSymbol("*") || IsSymbol("/"))
            {
                var op = Current.Text;
                _index++;
                left = Count(new RuleBinary(op, left, ParseUnary(depth + 1)));
            }

            return left;
        }

        private RuleNode ParseUnary(int depth)
        {
            CheckDepth(depth);
            if (IsSymbol("-"))
            {
                _index++;
                return Count(new RuleUnary("-", ParseUnary(depth + 1)));
            }

            return ParsePrimary(depth + 1);
        }

        private RuleNode ParsePrimary(int depth)
        {
            CheckDepth(depth);
            var token = Current;
            switch (token.Kind)
            {
                case TokenKind.Number:
                    _index++;
                    if (!decimal.TryParse(token.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
                        throw new RuleExpressionException("RULE_EXPRESSION_SYNTAX", $"'{token.Text}' is not a number.", token.Position);
                    return Count(new RuleLiteral(RuleValue.OfNumber(number)));
                case TokenKind.Text:
                    _index++;
                    return Count(new RuleLiteral(RuleValue.OfText(token.Text)));
                case TokenKind.Name:
                    _index++;
                    if (string.Equals(token.Text, "true", StringComparison.OrdinalIgnoreCase))
                        return Count(new RuleLiteral(RuleValue.OfBoolean(true)));
                    if (string.Equals(token.Text, "false", StringComparison.OrdinalIgnoreCase))
                        return Count(new RuleLiteral(RuleValue.OfBoolean(false)));
                    if (IsSymbol("("))
                        return ParseCall(token, depth + 1);
                    return Count(new RuleReference(token.Text, token.Position));
                case TokenKind.Symbol when token.Text == "(":
                    _index++;
                    var inner = ParseOr(depth + 1);
                    if (!IsSymbol(")"))
                        throw new RuleExpressionException("RULE_EXPRESSION_SYNTAX", "A bracket is not closed.", Current.Position);
                    _index++;
                    return inner;
                default:
                    throw new RuleExpressionException("RULE_EXPRESSION_SYNTAX",
                        token.Kind == TokenKind.End ? "The expression ends too soon." : $"Unexpected '{token.Text}'.",
                        token.Position);
            }
        }

        private RuleCall ParseCall(Token name, int depth)
        {
            CheckDepth(depth);
            _index++;
            var arguments = new List<RuleNode>();
            if (!IsSymbol(")"))
            {
                arguments.Add(ParseOr(depth + 1));
                while (IsSymbol(","))
                {
                    _index++;
                    arguments.Add(ParseOr(depth + 1));
                }
            }

            if (!IsSymbol(")"))
                throw new RuleExpressionException("RULE_EXPRESSION_SYNTAX", "A function call is not closed.", Current.Position);
            _index++;
            return Count(new RuleCall(name.Text.ToLowerInvariant(), arguments, name.Position));
        }
    }
}
