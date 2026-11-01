using System.Text.Json;
using WellSiteAutoPilot.Domain.Control;

namespace WellSiteAutoPilot.Application.Control;

/// <summary>A command a Logic Module says it may ask for, as its manifest's <c>commandRequirements</c> state it.</summary>
public sealed record DeclaredModuleCommand(string Command, string? Quantity, string? CanonicalUnit);

/// <summary>
/// What a module is allowed to ask for. The module author declares it, so a policy can only permit what the module can
/// actually request, and a command name written with the wrong case or spelling is caught when the policy is saved
/// rather than never matching at run time.
/// </summary>
public static class ModuleCommandDeclarations
{
    /// <summary>Null when the manifest cannot be read, which is different from a module that declares nothing.</summary>
    public static IReadOnlyList<DeclaredModuleCommand>? Read(string? manifestJson)
    {
        if (string.IsNullOrWhiteSpace(manifestJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(manifestJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            var list = Property(document.RootElement, "commandRequirements");
            if (list is not { ValueKind: JsonValueKind.Array } requirements) return null;
            var declared = new List<DeclaredModuleCommand>();
            foreach (var item in requirements.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) return null;
                if (Text(item, "command") is not { } command || string.IsNullOrWhiteSpace(command)) return null;
                declared.Add(new DeclaredModuleCommand(command, Text(item, "quantity"), Text(item, "canonicalUnit")));
            }

            return declared;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Problems with the commands a policy would permit. Names match exactly. A limit that states a unit must state the
    /// unit the module declares for that command, because the gate compares the unit of what the module sends.
    /// </summary>
    public static IReadOnlyList<string> Check(
        IReadOnlyCollection<AutonomyCommandLimit> limits, IReadOnlyList<DeclaredModuleCommand> declared)
    {
        var problems = new List<string>();
        foreach (var limit in limits)
        {
            var matches = declared.Where(item => string.Equals(item.Command, limit.Command, StringComparison.Ordinal)).ToArray();
            if (matches.Length == 0)
            {
                var known = declared.Count == 0
                    ? "this module declares no commands"
                    : "it declares " + string.Join(", ", declared.Select(item => item.Command).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
                problems.Add($"{limit.Command}: the logic's module does not declare this command ({known}).");
            }
            else if (limit.Unit is not null && matches.All(item => item.CanonicalUnit is not null &&
                         !string.Equals(item.CanonicalUnit, limit.Unit, StringComparison.Ordinal)))
            {
                problems.Add($"{limit.Command}: the module declares the unit '{matches[0].CanonicalUnit}', not '{limit.Unit}'.");
            }
        }

        return problems;
    }

    private static JsonElement? Property(JsonElement element, string camelCase)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, camelCase, StringComparison.OrdinalIgnoreCase)) return property.Value;
        }

        return null;
    }

    private static string? Text(JsonElement element, string camelCase) =>
        Property(element, camelCase) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
}
