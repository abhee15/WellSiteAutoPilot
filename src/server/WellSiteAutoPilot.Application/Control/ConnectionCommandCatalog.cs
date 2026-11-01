using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Application.Control;

/// <summary>
/// What engineering has switched on at one site for one connection and one adapter command: the hard ceiling that no
/// policy may exceed. Bounds are inclusive. An entry that is not <see cref="Enabled"/> carries no authority at all.
/// <paramref name="Target"/> is how the command is addressed at this site (the adapter's target fields); a module
/// never supplies it. Settle and deadline are the verification timing, chosen within the adapter's own bounds.
/// </summary>
public sealed record ConnectionCommandCatalogEntry(
    string Command,
    bool Enabled,
    decimal? Minimum,
    decimal? Maximum,
    decimal? MaximumStep,
    IReadOnlyDictionary<string, string> Target,
    int SettleSeconds,
    int DeadlineSeconds);

/// <summary>
/// The rules of the command catalog, kept pure so they can be proven without a database and cannot drift between the
/// place a catalog is saved and the place a command is dispatched. Nothing here has a default limit: an entry without
/// limits cannot be enabled, so no number can be mistaken for a safe one.
/// </summary>
public static class ConnectionCommandCatalogRules
{
    public const int MaximumEntries = AdapterCommandDefinitionValidator.MaximumCommands;

    /// <summary>
    /// Problems with a whole catalog measured against the adapter's definitions. An empty list means it may be saved.
    /// A command the adapter does not define is refused even when disabled, so the catalog never keeps a name the
    /// adapter cannot carry out.
    /// </summary>
    public static IReadOnlyList<string> Validate(
        AdapterDescriptor adapter, IReadOnlyCollection<ConnectionCommandCatalogEntry>? entries)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        var problems = new List<string>();
        if (entries is null)
        {
            problems.Add("The catalog is required (an empty list when nothing is enabled).");
            return problems;
        }

        if (entries.Count > MaximumEntries)
        {
            problems.Add($"A catalog holds at most {MaximumEntries} commands.");
            return problems;
        }

        var definitions = (adapter.Commands ?? [])
            .Where(definition => definition is not null)
            .GroupBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (entry is null)
            {
                problems.Add("A catalog entry is missing.");
                continue;
            }

            var name = entry.Command ?? string.Empty;
            var label = string.IsNullOrWhiteSpace(name) ? "(unnamed)" : name;
            if (!definitions.TryGetValue(name, out var definition))
            {
                problems.Add($"{label}: the adapter does not define this command.");
                continue;
            }

            if (!seen.Add(name))
            {
                problems.Add($"{label}: the command is listed more than once.");
                continue;
            }

            ValidateTiming(label, definition, entry, problems);
            if (entry.Target is null)
            {
                problems.Add($"{label}: the target is required (an empty map when the adapter has no target fields).");
            }
            else if (entry.Enabled)
            {
                ValidateTarget(label, definition, entry.Target, problems);
            }

            if (entry.Enabled)
            {
                ValidateLimits(label, definition, entry, problems);
            }
        }

        return problems;
    }

    /// <summary>
    /// Problems with a policy limit measured against the catalog entry for its command. A missing or disabled entry
    /// permits nothing, and a policy that leaves a bound open is refused: it would otherwise inherit a ceiling the
    /// person saving it never saw.
    /// </summary>
    public static IReadOnlyList<string> CheckPolicyLimit(
        string command, decimal? minimum, decimal? maximum, ConnectionCommandCatalogEntry? entry)
    {
        var problems = new List<string>();
        if (entry is null || !entry.Enabled)
        {
            problems.Add($"{command}: the connection's catalog does not enable this command.");
            return problems;
        }

        if (minimum is null || maximum is null)
        {
            problems.Add($"{command}: both a minimum and a maximum are required.");
            return problems;
        }

        if (minimum > maximum) problems.Add($"{command}: the minimum is above the maximum.");
        if (entry.Minimum is { } floor && minimum < floor)
            problems.Add($"{command}: the minimum is below the catalog's {floor}.");
        if (entry.Maximum is { } ceiling && maximum > ceiling)
            problems.Add($"{command}: the maximum is above the catalog's {ceiling}.");
        return problems;
    }

    /// <summary>True when moving from <paramref name="current"/> to <paramref name="requested"/> is within the catalog's step limit.</summary>
    public static bool StepAllowed(ConnectionCommandCatalogEntry entry, decimal current, decimal requested)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.MaximumStep is not { } step || Math.Abs(requested - current) <= step;
    }

    private static void ValidateTiming(
        string label, AdapterCommandDefinition definition, ConnectionCommandCatalogEntry entry, List<string> problems)
    {
        if (entry.SettleSeconds < 0 || entry.SettleSeconds > definition.MaximumSettleSeconds)
            problems.Add($"{label}: the settle time must be 0 to {definition.MaximumSettleSeconds} seconds.");
        if (entry.DeadlineSeconds < 1 || entry.DeadlineSeconds > definition.MaximumDeadlineSeconds)
            problems.Add($"{label}: the verification deadline must be 1 to {definition.MaximumDeadlineSeconds} seconds.");
        else if (entry.DeadlineSeconds <= entry.SettleSeconds)
            problems.Add($"{label}: the verification deadline must be after the settle time.");
    }

    private static void ValidateTarget(
        string label, AdapterCommandDefinition definition, IReadOnlyDictionary<string, string> target, List<string> problems)
    {
        var fields = definition.TargetFields ?? [];
        foreach (var key in target.Keys)
        {
            if (!fields.Any(field => string.Equals(field.Key, key, StringComparison.Ordinal)))
                problems.Add($"{label}: '{key}' is not a target field of this command.");
        }

        foreach (var field in fields)
        {
            target.TryGetValue(field.Key, out var value);
            if (string.IsNullOrWhiteSpace(value))
            {
                if (field.Required) problems.Add($"{label}: the target field '{field.Label}' is required.");
            }
            else if (value.Length > field.MaxLength)
            {
                problems.Add($"{label}: the target field '{field.Label}' is longer than {field.MaxLength} characters.");
            }
            else if (value.Any(char.IsControl))
            {
                problems.Add($"{label}: the target field '{field.Label}' contains control characters.");
            }
        }
    }

    private static void ValidateLimits(
        string label, AdapterCommandDefinition definition, ConnectionCommandCatalogEntry entry, List<string> problems)
    {
        if (entry.Minimum is not { } minimum || entry.Maximum is not { } maximum)
        {
            problems.Add($"{label}: a minimum and a maximum are required before a command can be enabled.");
            return;
        }

        if (minimum > maximum) problems.Add($"{label}: the minimum is above the maximum.");
        if (definition.ValueKind == AdapterCommandValueKind.OnOff)
        {
            if (minimum != 0 || maximum != 1)
                problems.Add($"{label}: an on/off command has a minimum of 0 and a maximum of 1.");
            if (entry.MaximumStep is not null)
                problems.Add($"{label}: an on/off command has no step limit.");
            return;
        }

        if (definition.ValueKind == AdapterCommandValueKind.WholeNumber &&
            (decimal.Truncate(minimum) != minimum || decimal.Truncate(maximum) != maximum))
            problems.Add($"{label}: the limits of a whole-number command must be whole numbers.");
        if (entry.MaximumStep is { } step)
        {
            if (step <= 0) problems.Add($"{label}: the step limit must be above zero.");
            else if (definition.ValueKind == AdapterCommandValueKind.WholeNumber && decimal.Truncate(step) != step)
                problems.Add($"{label}: the step limit of a whole-number command must be a whole number.");
        }
    }
}

/// <summary>A connection's catalog as stored: <see cref="Version"/> is what the next change must quote.</summary>
public sealed record ConnectionCommandCatalog(
    Guid ConnectionId,
    int Version,
    string ChangedBy,
    DateTimeOffset ChangedAtUtc,
    string? Reason,
    IReadOnlyList<ConnectionCommandCatalogEntry> Entries);

public static class ConnectionCommandCatalogFailureCodes
{
    public const string Invalid = "COMMAND_CATALOG_INVALID";
    public const string Stale = "COMMAND_CATALOG_STALE";
}

public interface IConnectionCommandCatalogRepository
{
    /// <summary>Null when no catalog was ever saved for the connection, which permits no command at all.</summary>
    Task<ConnectionCommandCatalog?> FindAsync(Guid connectionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the whole catalog if <paramref name="expectedVersion"/> is the stored version (0 when none is stored).
    /// The change, the catalog it produced and an audit event are written together. Needs both control.manage and
    /// connections.manage. The caller has already checked the entries against the adapter's definitions.
    /// </summary>
    Task<ConnectionCommandCatalog> ReplaceAuthorizedAsync(
        Guid connectionId, int expectedVersion, IReadOnlyCollection<ConnectionCommandCatalogEntry> entries, string? reason,
        Security.AccessSubject subject, Security.SecurityActorContext actor, CancellationToken cancellationToken = default);
}
