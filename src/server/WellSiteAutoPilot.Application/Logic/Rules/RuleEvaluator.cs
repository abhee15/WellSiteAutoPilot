namespace WellSiteAutoPilot.Application.Logic.Rules;

/// <summary>
/// Checks an expression against the kinds of the values it may read, and evaluates it. Both walk the same tree so
/// what authoring accepts is exactly what a run evaluates.
/// </summary>
public static class RuleEvaluator
{
    private static readonly Dictionary<string, (int Min, int Max)> FunctionArity = new(StringComparer.Ordinal)
    {
        ["min"] = (2, 8),
        ["max"] = (2, 8),
        ["avg"] = (1, 8),
        ["abs"] = (1, 1),
        ["round"] = (1, 2),
        ["clamp"] = (3, 3),
        ["if"] = (3, 3)
    };

    public static IReadOnlyCollection<string> FunctionNames => FunctionArity.Keys;

    /// <summary>Returns the kind the expression produces, or throws with the reason it cannot be used.</summary>
    public static RuleValueKind Check(RuleNode node, IReadOnlyDictionary<string, RuleValueKind> known)
    {
        switch (node)
        {
            case RuleLiteral literal:
                return literal.Value.Kind;
            case RuleReference reference:
                if (!known.TryGetValue(reference.Name, out var kind))
                    throw new RuleExpressionException("RULE_EXPRESSION_UNKNOWN_NAME",
                        $"'{reference.Name}' is not a value in this module.", reference.Position);
                return kind;
            case RuleUnary unary:
                var operand = Check(unary.Operand, known);
                Require(operand, unary.Operator == "not" ? RuleValueKind.Boolean : RuleValueKind.Number, unary.Operator);
                return operand;
            case RuleBinary binary:
                return CheckBinary(binary, known);
            case RuleCall call:
                return CheckCall(call, known);
            default:
                throw new RuleExpressionException("RULE_EXPRESSION_SYNTAX", "Unsupported expression.");
        }
    }

    private static RuleValueKind CheckBinary(RuleBinary binary, IReadOnlyDictionary<string, RuleValueKind> known)
    {
        var left = Check(binary.Left, known);
        var right = Check(binary.Right, known);
        switch (binary.Operator)
        {
            case "+" or "-" or "*" or "/":
                Require(left, RuleValueKind.Number, binary.Operator);
                Require(right, RuleValueKind.Number, binary.Operator);
                return RuleValueKind.Number;
            case "<" or "<=" or ">" or ">=":
                Require(left, RuleValueKind.Number, binary.Operator);
                Require(right, RuleValueKind.Number, binary.Operator);
                return RuleValueKind.Boolean;
            case "==" or "!=":
                if (left != right)
                    throw new RuleExpressionException("RULE_EXPRESSION_TYPE", "Both sides of a comparison must be the same kind.");
                return RuleValueKind.Boolean;
            default:
                Require(left, RuleValueKind.Boolean, binary.Operator);
                Require(right, RuleValueKind.Boolean, binary.Operator);
                return RuleValueKind.Boolean;
        }
    }

    private static RuleValueKind CheckCall(RuleCall call, IReadOnlyDictionary<string, RuleValueKind> known)
    {
        if (!FunctionArity.TryGetValue(call.Name, out var arity))
            throw new RuleExpressionException("RULE_EXPRESSION_UNKNOWN_FUNCTION",
                $"'{call.Name}' is not a function.", call.Position);
        if (call.Arguments.Count < arity.Min || call.Arguments.Count > arity.Max)
            throw new RuleExpressionException("RULE_EXPRESSION_ARGUMENTS",
                $"'{call.Name}' takes {arity.Min}{(arity.Max == arity.Min ? string.Empty : $" to {arity.Max}")} values.", call.Position);

        var kinds = call.Arguments.Select(argument => Check(argument, known)).ToList();
        if (call.Name == "if")
        {
            Require(kinds[0], RuleValueKind.Boolean, "if");
            if (kinds[1] != kinds[2])
                throw new RuleExpressionException("RULE_EXPRESSION_TYPE", "Both results of 'if' must be the same kind.", call.Position);
            return kinds[1];
        }

        foreach (var kind in kinds) Require(kind, RuleValueKind.Number, call.Name);
        return RuleValueKind.Number;
    }

    private static void Require(RuleValueKind actual, RuleValueKind expected, string what)
    {
        if (actual != expected)
            throw new RuleExpressionException("RULE_EXPRESSION_TYPE", $"'{what}' needs {expected.ToString().ToLowerInvariant()} values.");
    }

    /// <summary>Evaluates a checked expression. Division by zero and missing values fail the run with a code.</summary>
    public static RuleValue Evaluate(RuleNode node, IReadOnlyDictionary<string, RuleValue> values)
    {
        switch (node)
        {
            case RuleLiteral literal:
                return literal.Value;
            case RuleReference reference:
                return values.TryGetValue(reference.Name, out var value)
                    ? value
                    : throw new RuleExpressionException("RULE_EXPRESSION_MISSING_VALUE",
                        $"'{reference.Name}' has no value.", reference.Position);
            case RuleUnary unary:
                var operand = Evaluate(unary.Operand, values);
                return unary.Operator == "not" ? RuleValue.OfBoolean(!operand.Boolean) : RuleValue.OfNumber(-operand.Number);
            case RuleBinary binary:
                return EvaluateBinary(binary, values);
            case RuleCall call:
                return EvaluateCall(call, values);
            default:
                throw new RuleExpressionException("RULE_EXPRESSION_SYNTAX", "Unsupported expression.");
        }
    }

    private static RuleValue EvaluateBinary(RuleBinary binary, IReadOnlyDictionary<string, RuleValue> values)
    {
        var left = Evaluate(binary.Left, values);
        if (binary.Operator == "and" && !left.Boolean) return RuleValue.OfBoolean(false);
        if (binary.Operator == "or" && left.Boolean) return RuleValue.OfBoolean(true);
        var right = Evaluate(binary.Right, values);
        try
        {
            return binary.Operator switch
            {
                "+" => RuleValue.OfNumber(left.Number + right.Number),
                "-" => RuleValue.OfNumber(left.Number - right.Number),
                "*" => RuleValue.OfNumber(left.Number * right.Number),
                "/" => right.Number == 0m
                    ? throw new RuleExpressionException("RULE_EXPRESSION_DIVIDE_BY_ZERO", "Division by zero.")
                    : RuleValue.OfNumber(left.Number / right.Number),
                "<" => RuleValue.OfBoolean(left.Number < right.Number),
                "<=" => RuleValue.OfBoolean(left.Number <= right.Number),
                ">" => RuleValue.OfBoolean(left.Number > right.Number),
                ">=" => RuleValue.OfBoolean(left.Number >= right.Number),
                "==" => RuleValue.OfBoolean(left.Equals(right)),
                "!=" => RuleValue.OfBoolean(!left.Equals(right)),
                _ => RuleValue.OfBoolean(right.Boolean)
            };
        }
        catch (OverflowException)
        {
            throw new RuleExpressionException("RULE_EXPRESSION_OVERFLOW", "A number is too large.");
        }
    }

    private static RuleValue EvaluateCall(RuleCall call, IReadOnlyDictionary<string, RuleValue> values)
    {
        if (call.Name == "if")
            return Evaluate(call.Arguments[0], values).Boolean
                ? Evaluate(call.Arguments[1], values)
                : Evaluate(call.Arguments[2], values);

        var numbers = call.Arguments.Select(argument => Evaluate(argument, values).Number).ToList();
        return call.Name switch
        {
            "min" => RuleValue.OfNumber(numbers.Min()),
            "max" => RuleValue.OfNumber(numbers.Max()),
            "avg" => RuleValue.OfNumber(numbers.Sum() / numbers.Count),
            "abs" => RuleValue.OfNumber(Math.Abs(numbers[0])),
            "round" => RuleValue.OfNumber(Math.Round(numbers[0],
                numbers.Count > 1 ? (int)Math.Clamp(numbers[1], 0m, 10m) : 0, MidpointRounding.AwayFromZero)),
            _ => RuleValue.OfNumber(Math.Min(Math.Max(numbers[0], numbers[1]), numbers[2]))
        };
    }
}
