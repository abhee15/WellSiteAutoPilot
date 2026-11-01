using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

// Disposable authoritative evaluator for two decimal operations, not a product runtime.
internal static class DecimalDocumentProbe
{
    public static async Task Run(string path)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            if (bytes.Length > 65536) throw new InvalidOperationException("DOCUMENT_TOO_LARGE");
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.GetProperty("profile").GetString() != "probe-decimal-v1") throw new InvalidOperationException("PROFILE_UNSUPPORTED");
            var values = new Dictionary<string, (decimal Value, string Unit)>(StringComparer.Ordinal);
            var nodes = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            string Id(JsonElement item)
            {
                var id = item.GetProperty("id").GetString() ?? "";
                if (!Regex.IsMatch(id, "^[a-z][a-z0-9-]{0,63}$")) throw new InvalidOperationException("ID_INVALID");
                return id;
            }
            foreach (var input in root.GetProperty("inputs").EnumerateArray())
            {
                var literal = input.GetProperty("literal");
                if (literal.ValueKind != JsonValueKind.String) throw new InvalidOperationException("DECIMAL_TEXT_REQUIRED");
                var text = literal.GetString()!;
                if (!Regex.IsMatch(text, "^-?(0|[1-9][0-9]*)(\\.[0-9]+)?$") ||
                    !decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ||
                    value.ToString(CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.') != text.TrimEnd('0').TrimEnd('.'))
                    throw new InvalidOperationException("DECIMAL_LITERAL_INVALID");
                if (!values.TryAdd(Id(input), (value, input.GetProperty("unit").GetString()!))) throw new InvalidOperationException("ID_DUPLICATE");
            }
            foreach (var node in root.GetProperty("nodes").EnumerateArray())
            {
                var id = Id(node);
                if (values.ContainsKey(id) || !nodes.TryAdd(id, node)) throw new InvalidOperationException("ID_DUPLICATE");
            }
            if (nodes.Count > 16 || values.Count > 32) throw new InvalidOperationException("DOCUMENT_LIMIT");
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            (decimal Value, string Unit) Visit(string id)
            {
                if (values.TryGetValue(id, out var existing)) return existing;
                if (!nodes.TryGetValue(id, out var node)) throw new InvalidOperationException("DEPENDENCY_MISSING");
                if (!visiting.Add(id)) throw new InvalidOperationException("DEPENDENCY_CYCLE");
                var left = Visit(node.GetProperty("left").GetString()!);
                var right = Visit(node.GetProperty("right").GetString()!);
                if (left.Unit != right.Unit) throw new InvalidOperationException("UNIT_MISMATCH");
                var value = node.GetProperty("operation").GetString() switch
                {
                    "difference" => (checked(left.Value - right.Value), left.Unit),
                    "ratio" => (left.Value / right.Value, "1"),
                    _ => throw new InvalidOperationException("OPERATION_UNSUPPORTED")
                };
                visiting.Remove(id); values[id] = value; return value;
            }
            var outputs = nodes.Keys.Select(id => { var value = Visit(id); return new { id, literal = value.Value.ToString(CultureInfo.InvariantCulture), unit = value.Unit }; }).ToArray();
            Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", document = root, outputs }));
        }
        catch (Exception error)
        {
            var code = error switch { DivideByZeroException => "DIVIDE_BY_ZERO", OverflowException => "DECIMAL_OVERFLOW", InvalidOperationException => error.Message, _ => "DOCUMENT_INVALID" };
            Console.WriteLine(JsonSerializer.Serialize(new { status = "rejected", code }));
        }
    }
}
