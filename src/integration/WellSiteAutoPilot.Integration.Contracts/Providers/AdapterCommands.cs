using System.Text.RegularExpressions;

namespace WellSiteAutoPilot.Integration.Contracts.Providers;

public enum AdapterCommandValueKind
{
    Number = 0,
    WholeNumber = 1,

    /// <summary>0 is off and 1 is on.</summary>
    OnOff = 2
}

public enum AdapterCommandVerification
{
    /// <summary>A read-only measurement confirms the value took effect.</summary>
    ReadBack = 0,

    /// <summary>The provider only acknowledges. An acknowledgement is never treated as verified.</summary>
    AcknowledgementOnly = 1
}

/// <summary>
/// One command an adapter can carry out: its <b>shape</b>, never a limit. How far a value may move is engineering
/// at the site and lives in the connection's command catalog, so nothing here can be mistaken for a safe value.
/// <paramref name="Name"/> is the platform vocabulary a module's command requirement is matched against.
/// <paramref name="TargetFields"/> says how a command is addressed at this provider; a module can never supply them.
/// The settle and deadline values are adapter-owned bounds that a site chooses within.
/// </summary>
public sealed record AdapterCommandDefinition(
    string Name,
    string DisplayName,
    string Description,
    string Quantity,
    string? Unit,
    AdapterCommandValueKind ValueKind,
    IReadOnlyList<AdapterSettingField> TargetFields,
    AdapterCommandVerification Verification,
    string? ReadbackQuantity,
    int DefaultSettleSeconds,
    int MaximumSettleSeconds,
    int DefaultDeadlineSeconds,
    int MaximumDeadlineSeconds);

/// <summary>
/// Checks what an adapter says about its commands. Pure, so every shipped descriptor can be held to it in CI: an adapter
/// advertises the Command capability exactly when it defines commands, and a read-back command names what to read.
/// </summary>
public static partial class AdapterCommandDefinitionValidator
{
    public const int MaximumCommands = 64;

    /// <summary>Names that are never a command (the platform's own advisory marker).</summary>
    public static readonly IReadOnlyCollection<string> ReservedNames = ["ADVISORY_REVIEW"];

    public static IReadOnlyList<string> Validate(AdapterDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var problems = new List<string>();
        var commands = descriptor.Commands ?? [];
        var advertisesCommand = descriptor.Capabilities.Any(item => item.Kind == ProviderCapabilityKind.Command);
        if (commands.Count > MaximumCommands) problems.Add($"At most {MaximumCommands} commands can be defined.");
        if (advertisesCommand != (commands.Count > 0))
            problems.Add("The Command capability must be advertised exactly when commands are defined.");
        var advertisesVerification = descriptor.Capabilities.Any(item => item.Kind == ProviderCapabilityKind.Verification);
        if (commands.Any(item => item.Verification == AdapterCommandVerification.ReadBack) && !advertisesVerification)
            problems.Add("A read-back command needs the Verification capability to be advertised.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in commands)
        {
            if (command is null)
            {
                problems.Add("A command definition is missing.");
                continue;
            }

            // A definition read from JSON can carry a null name even though the type says otherwise.
            var name = command.Name ?? string.Empty;
            var label = string.IsNullOrWhiteSpace(name) ? "(unnamed)" : name;
            if (!Identifier().IsMatch(name)) problems.Add($"{label}: the name must be 1 to 128 letters, digits, '.', '_' or '-'.");
            else if (ReservedNames.Contains(name, StringComparer.OrdinalIgnoreCase)) problems.Add($"{label}: the name is reserved.");
            else if (!names.Add(name)) problems.Add($"{label}: the name is defined more than once (names differing only by case are the same command).");
            if (string.IsNullOrWhiteSpace(command.DisplayName) || string.IsNullOrWhiteSpace(command.Description))
                problems.Add($"{label}: a display name and description are required.");
            if (string.IsNullOrWhiteSpace(command.Quantity)) problems.Add($"{label}: the quantity is required.");
            if (!Enum.IsDefined(command.ValueKind)) problems.Add($"{label}: the value kind is unknown.");
            if (command.ValueKind == AdapterCommandValueKind.OnOff && !string.IsNullOrWhiteSpace(command.Unit))
                problems.Add($"{label}: an on/off command has no unit.");
            if (command.TargetFields is null) problems.Add($"{label}: the target fields are required (an empty list when none).");
            else if (command.TargetFields.Select(field => field.Key).Distinct(StringComparer.Ordinal).Count() != command.TargetFields.Count)
                problems.Add($"{label}: target field keys must be unique.");

            if (!Enum.IsDefined(command.Verification)) problems.Add($"{label}: the verification mode is unknown.");
            else if (command.Verification == AdapterCommandVerification.ReadBack && string.IsNullOrWhiteSpace(command.ReadbackQuantity))
                problems.Add($"{label}: a read-back command must name the quantity to read.");
            else if (command.Verification == AdapterCommandVerification.AcknowledgementOnly && command.ReadbackQuantity is not null)
                problems.Add($"{label}: an acknowledgement-only command has nothing to read back.");

            if (command.DefaultSettleSeconds < 0 || command.DefaultSettleSeconds > command.MaximumSettleSeconds)
                problems.Add($"{label}: the default settle time must be between 0 and its maximum.");
            if (command.MaximumDeadlineSeconds < 1 || command.DefaultDeadlineSeconds < 1 ||
                command.DefaultDeadlineSeconds > command.MaximumDeadlineSeconds)
                problems.Add($"{label}: the default deadline must be between 1 second and its maximum.");
            if (command.DefaultDeadlineSeconds <= command.DefaultSettleSeconds || command.MaximumDeadlineSeconds <= command.MaximumSettleSeconds)
                problems.Add($"{label}: a deadline must be later than its settle time.");
        }

        return problems;
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();
}
