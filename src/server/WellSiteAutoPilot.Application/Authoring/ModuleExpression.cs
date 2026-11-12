using System.Globalization;

namespace WellSiteAutoPilot.Application.Authoring;

public sealed class ExpressionException(string message, int position) : Exception(message)
{
    public int Position { get; } = position;
}

internal abstract record Node;
internal sealed record NumberNode(decimal Value) : Node;
internal sealed record TextNode(string Value) : Node;
internal sealed record FlagNode(bool Value) : Node;
internal sealed record RefNode(string Name, int Position) : Node;
internal sealed record UnaryNode(string Op, Node Operand) : Node;
internal sealed record BinaryNode(string Op, Node Left, Node Right) : Node;
internal sealed record CallNode(string Function, IReadOnlyList<Node> Arguments) : Node;

/// <summary>
/// A deliberately small, side-effect-free expression language for logic module calculations: arithmetic,
/// comparison, and, or, not, and a fixed set of functions. There is no loop, no assignment and no host access, so
/// an expression can be validated, traced and evaluated safely on the server or in tests. Values are decimal, the
/// same type the module SDK uses, and carry units: quantities with different units are converted when they measure
/// the same thing and refused when they do not. This is language version <see cref="LanguageVersion"/>; a change to
/// what an expression means must bump it, so an older module is never silently reinterpreted.
/// </summary>
public sealed class ModuleExpression
{
    public const int LanguageVersion = 1;
    public const int MaxLength = 500;
    public static readonly IReadOnlyCollection<string> Functions =
        ["if", "min", "max", "abs", "round", "avg", "clamp", "coalesce", "has"];

    private readonly Node _root;

    private ModuleExpression(Node root, IReadOnlyList<string> references)
    {
        _root = root;
        References = references;
    }

    /// <summary>Names the expression reads, in first-use order.</summary>
    public IReadOnlyList<string> References { get; }

    public static ModuleExpression Parse(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new ExpressionException("Enter an expression.", 0);
        if (source.Length > MaxLength)
            throw new ExpressionException($"An expression can be at most {MaxLength} characters.", MaxLength);
        var parser = new Parser(source);
        var root = parser.ParseAll();
        return new ModuleExpression(root, parser.References);
    }

    public Val Evaluate(Func<string, Val> lookup)
    {
        try
        {
            return Eval(_root, lookup);
        }
        catch (OverflowException)
        {
            return Val.Missing("the result is out of range");
        }
    }

    private static Val Eval(Node node, Func<string, Val> lookup) => node switch
    {
        NumberNode n => Val.Of(n.Value),
        TextNode t => Val.OfText(t.Value),
        FlagNode f => Val.Of(f.Value),
        RefNode r => lookup(r.Name),
        UnaryNode u => Unary(u, lookup),
        BinaryNode b => Binary(b, lookup),
        CallNode c => Call(c, lookup),
        _ => Val.Missing("unsupported expression")
    };

    private static Val Unary(UnaryNode node, Func<string, Val> lookup)
    {
        var value = Eval(node.Operand, lookup);
        if (!value.Available)
            return value;
        if (node.Op == "-")
            return value.Kind == ValueKind.Number ? value with { Number = -value.Number!.Value } : Val.Missing("'-' needs a number");
        return value.Kind == ValueKind.Flag ? Val.Of(!value.Flag!.Value).Derived(value) : Val.Missing("'!' needs a true or false value");
    }

    /// <summary>
    /// Brings two numbers to one unit so they can be added or compared. A plain number takes the other side's unit;
    /// two units convert when they measure the same thing and fail, with the reason, when they do not.
    /// </summary>
    private static bool Unify(string op, Val left, Val right, out decimal a, out decimal b, out string? unit, out string? problem)
    {
        a = left.Number!.Value;
        b = right.Number!.Value;
        unit = left.Unit ?? right.Unit;
        problem = null;
        if (left.Unit is null || right.Unit is null || left.Unit == right.Unit)
            return true;
        if (Units.TryConvert(b, right.Unit, left.Unit, out var converted, out var why))
        {
            b = converted;
            return true;
        }
        problem = $"'{op}' cannot combine {left.Unit} and {right.Unit}: {why}";
        return false;
    }

    private static Val Binary(BinaryNode node, Func<string, Val> lookup)
    {
        // A decided logical result does not need the other side, so a missing input can still leave a clear answer.
        if (node.Op is "&&" or "||")
            return Logical(node, lookup);
        var left = Eval(node.Left, lookup);
        var right = Eval(node.Right, lookup);
        if (!left.Available) return left;
        if (!right.Available) return right;
        if (node.Op is "==" or "!=")
        {
            bool same;
            if (left.Kind == ValueKind.Number && right.Kind == ValueKind.Number)
            {
                if (!Unify(node.Op, left, right, out var x, out var y, out _, out var mismatch))
                    return Val.Missing(mismatch!);
                same = x == y;
            }
            else
            {
                same = left.Kind == right.Kind && left.Kind switch
                {
                    ValueKind.Flag => left.Flag == right.Flag,
                    _ => string.Equals(left.Text, right.Text, StringComparison.Ordinal)
                };
            }
            return Val.Of(node.Op == "==" ? same : !same).Derived(left, right);
        }
        if (node.Op == "+" && left.Kind == ValueKind.Text && right.Kind == ValueKind.Text)
            return Val.OfText(left.Text + right.Text).Derived(left, right);
        if (left.Kind != ValueKind.Number || right.Kind != ValueKind.Number)
            return Val.Missing($"'{node.Op}' needs numbers");

        decimal p, q;
        string? unit;
        switch (node.Op)
        {
            case "*":
                if (left.Unit is not null && right.Unit is not null)
                    return Val.Missing($"'*' cannot multiply {left.Unit} by {right.Unit}: this version does not combine two quantities that have units.");
                return Val.Of(left.Number!.Value * right.Number!.Value, left.Unit ?? right.Unit).Derived(left, right);
            case "/":
                if (right.Number == 0) return Val.Missing("division by zero");
                if (left.Unit is null && right.Unit is not null)
                    return Val.Missing($"'/' cannot divide a plain number by {right.Unit}: this version does not produce inverse units.");
                if (left.Unit is not null && right.Unit is not null)
                {
                    if (!Unify("/", left, right, out p, out q, out _, out var problem)) return Val.Missing(problem!);
                    return (q == 0 ? Val.Missing("division by zero") : Val.Of(p / q)).Derived(left, right);
                }
                return Val.Of(left.Number!.Value / right.Number!.Value, left.Unit).Derived(left, right);
        }

        if (!Unify(node.Op, left, right, out p, out q, out unit, out var mismatchProblem))
            return Val.Missing(mismatchProblem!);
        Val result = node.Op switch
        {
            "+" => Val.Of(p + q, unit),
            "-" => Val.Of(p - q, unit),
            "%" => q == 0 ? Val.Missing("division by zero") : Val.Of(p % q, unit),
            "<" => Val.Of(p < q),
            "<=" => Val.Of(p <= q),
            ">" => Val.Of(p > q),
            ">=" => Val.Of(p >= q),
            _ => Val.Missing("unsupported operator")
        };
        return result.Available ? result.Derived(left, right) : result;
    }

    private static Val Logical(BinaryNode node, Func<string, Val> lookup)
    {
        var and = node.Op == "&&";
        var left = Eval(node.Left, lookup);
        if (left is { Kind: ValueKind.Flag } && left.Flag == !and)
            return left;
        var right = Eval(node.Right, lookup);
        if (right is { Kind: ValueKind.Flag } && right.Flag == !and)
            return right;
        if (!left.Available) return left;
        if (!right.Available) return right;
        if (left.Kind != ValueKind.Flag || right.Kind != ValueKind.Flag)
            return Val.Missing($"'{node.Op}' needs true or false values");
        return Val.Of(and ? left.Flag!.Value && right.Flag!.Value : left.Flag!.Value || right.Flag!.Value).Derived(left, right);
    }

    private static Val Call(CallNode call, Func<string, Val> lookup)
    {
        var args = call.Arguments;
        switch (call.Function)
        {
            case "if":
            {
                if (args.Count != 3) return Val.Missing("if(condition, then, otherwise) takes 3 values");
                var condition = Eval(args[0], lookup);
                if (!condition.Available) return condition;
                if (condition.Kind != ValueKind.Flag) return Val.Missing("if() needs a true or false condition");
                var chosen = Eval(condition.Flag!.Value ? args[1] : args[2], lookup);
                return chosen.Available ? chosen.Derived(condition) : chosen;
            }
            case "coalesce":
            {
                foreach (var arg in args)
                {
                    var value = Eval(arg, lookup);
                    if (value.Available) return value;
                }
                return Val.Missing("none of the coalesce() values are available");
            }
            case "has":
                return args.Count == 1 ? Val.Of(Eval(args[0], lookup).Available) : Val.Missing("has(value) takes 1 value");
        }

        var values = new List<Val>();
        foreach (var arg in args)
        {
            var value = Eval(arg, lookup);
            if (!value.Available) return value;
            if (value.Kind != ValueKind.Number) return Val.Missing($"{call.Function}() needs numbers");
            values.Add(value);
        }

        // round(value, digits) has a plain count for the second argument; every other argument is one quantity.
        var quantities = call.Function == "round" ? values.Take(1).ToList() : values;
        if (quantities.Count == 0) return Val.Missing($"{call.Function}() was given the wrong number of values");
        var unit = quantities.Select(v => v.Unit).FirstOrDefault(u => u is not null);
        var numbers = new List<decimal>();
        foreach (var quantity in quantities)
        {
            var number = quantity.Number!.Value;
            if (quantity.Unit is not null && unit is not null && quantity.Unit != unit)
            {
                if (!Units.TryConvert(number, quantity.Unit, unit, out number, out var why))
                    return Val.Missing($"{call.Function}() cannot combine {unit} and {quantity.Unit}: {why}");
            }
            numbers.Add(number);
        }
        var result = call.Function switch
        {
            "abs" when values.Count == 1 => Val.Of(Math.Abs(numbers[0]), unit),
            "round" when values.Count is 1 or 2 => Val.Of(Math.Round(numbers[0], values.Count == 2 ? (int)Math.Clamp(values[1].Number!.Value, 0, 10) : 0, MidpointRounding.AwayFromZero), unit),
            "min" => Val.Of(numbers.Min(), unit),
            "max" => Val.Of(numbers.Max(), unit),
            "avg" => Val.Of(numbers.Average(), unit),
            "clamp" when values.Count == 3 => Val.Of(Math.Clamp(numbers[0], Math.Min(numbers[1], numbers[2]), Math.Max(numbers[1], numbers[2])), unit),
            _ => Val.Missing($"{call.Function}() was given the wrong number of values")
        };
        return result.Available ? result.Derived([.. values]) : result;
    }

    private sealed class Parser(string text)
    {
        private int _at;
        private readonly List<string> _references = [];
        public IReadOnlyList<string> References => _references;

        public Node ParseAll()
        {
            var node = Or();
            Skip();
            if (_at < text.Length)
                throw new ExpressionException($"Unexpected '{text[_at]}'.", _at);
            return node;
        }

        private void Skip()
        {
            while (_at < text.Length && char.IsWhiteSpace(text[_at])) _at++;
        }

        private bool Accept(string token)
        {
            Skip();
            if (string.CompareOrdinal(text, _at, token, 0, token.Length) != 0) return false;
            _at += token.Length;
            return true;
        }

        private Node Or()
        {
            var left = And();
            while (Accept("||")) left = new BinaryNode("||", left, And());
            return left;
        }

        private Node And()
        {
            var left = Equality();
            while (Accept("&&")) left = new BinaryNode("&&", left, Equality());
            return left;
        }

        private Node Equality()
        {
            var left = Comparison();
            while (true)
            {
                if (Accept("==")) left = new BinaryNode("==", left, Comparison());
                else if (Accept("!=")) left = new BinaryNode("!=", left, Comparison());
                else return left;
            }
        }

        private Node Comparison()
        {
            var left = Additive();
            while (true)
            {
                if (Accept("<=")) left = new BinaryNode("<=", left, Additive());
                else if (Accept(">=")) left = new BinaryNode(">=", left, Additive());
                else if (Accept("<")) left = new BinaryNode("<", left, Additive());
                else if (Accept(">")) left = new BinaryNode(">", left, Additive());
                else return left;
            }
        }

        private Node Additive()
        {
            var left = Multiplicative();
            while (true)
            {
                if (Accept("+")) left = new BinaryNode("+", left, Multiplicative());
                else if (Accept("-")) left = new BinaryNode("-", left, Multiplicative());
                else return left;
            }
        }

        private Node Multiplicative()
        {
            var left = UnaryOperand();
            while (true)
            {
                if (Accept("*")) left = new BinaryNode("*", left, UnaryOperand());
                else if (Accept("/")) left = new BinaryNode("/", left, UnaryOperand());
                else if (Accept("%")) left = new BinaryNode("%", left, UnaryOperand());
                else return left;
            }
        }

        private Node UnaryOperand()
        {
            Skip();
            if (_at < text.Length && text[_at] == '-') { _at++; return new UnaryNode("-", UnaryOperand()); }
            if (_at < text.Length && text[_at] == '!' && (_at + 1 >= text.Length || text[_at + 1] != '=')) { _at++; return new UnaryNode("!", UnaryOperand()); }
            return Primary();
        }

        private Node Primary()
        {
            Skip();
            if (_at >= text.Length)
                throw new ExpressionException("The expression ends too soon.", _at);
            var c = text[_at];
            if (c == '(')
            {
                _at++;
                var inner = Or();
                if (!Accept(")")) throw new ExpressionException("Missing ')'.", _at);
                return inner;
            }
            if (char.IsAsciiDigit(c) || c == '.')
                return Number();
            if (c is '"' or '\'')
                return Quoted(c);
            if (char.IsAsciiLetter(c) || c == '_')
                return Name();
            throw new ExpressionException($"Unexpected '{c}'.", _at);
        }

        private NumberNode Number()
        {
            var start = _at;
            while (_at < text.Length && (char.IsAsciiDigit(text[_at]) || text[_at] == '.')) _at++;
            if (!decimal.TryParse(text.AsSpan(start, _at - start), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
                throw new ExpressionException("That is not a valid number.", start);
            return new NumberNode(value);
        }

        private TextNode Quoted(char quote)
        {
            var start = ++_at;
            while (_at < text.Length && text[_at] != quote) _at++;
            if (_at >= text.Length) throw new ExpressionException("Text is missing its closing quote.", start - 1);
            var value = text[start.._at];
            _at++;
            return new TextNode(value);
        }

        private Node Name()
        {
            var start = _at;
            while (_at < text.Length && (char.IsAsciiLetterOrDigit(text[_at]) || text[_at] == '_')) _at++;
            var name = text[start.._at];
            if (name is "true" or "false") return new FlagNode(name == "true");
            Skip();
            if (_at < text.Length && text[_at] == '(')
            {
                if (!Functions.Contains(name))
                    throw new ExpressionException($"'{name}' is not a function. Available: {string.Join(", ", Functions)}.", start);
                _at++;
                var args = new List<Node>();
                if (!Accept(")"))
                {
                    do args.Add(Or()); while (Accept(","));
                    if (!Accept(")")) throw new ExpressionException("Missing ')'.", _at);
                }
                return new CallNode(name, args);
            }
            if (!_references.Contains(name, StringComparer.Ordinal)) _references.Add(name);
            return new RefNode(name, start);
        }
    }
}
