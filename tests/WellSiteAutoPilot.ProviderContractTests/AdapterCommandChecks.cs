using System.Text.Json;
using WellSiteAutoPilot.Integration.Contracts.Providers;

/// <summary>An adapter's command definitions are held to one validator, and survive the Gateway's JSON unchanged.</summary>
internal static class AdapterCommandChecks
{
    private static readonly ProviderCapability[] Both =
    [
        new(ProviderCapabilityKind.Command, "Governed commands"),
        new(ProviderCapabilityKind.Verification, "Command verification")
    ];

    private static AdapterCommandDefinition Command(string name = "set-speed", AdapterCommandVerification verification = AdapterCommandVerification.ReadBack,
        string? readback = "Speed", int settle = 5, int maxSettle = 30, int deadline = 60, int maxDeadline = 300, string? unit = "%",
        AdapterCommandValueKind kind = AdapterCommandValueKind.Number, IReadOnlyList<AdapterSettingField>? target = null) =>
        new(name, "Set speed", "Sets the speed.", "SpeedSetpoint", unit, kind,
            target ?? [new AdapterSettingField("setpoint", "Setpoint", "The setpoint name.", AdapterSettingKind.Text, true)],
            verification, readback, settle, maxSettle, deadline, maxDeadline);

    private static AdapterDescriptor Adapter(IReadOnlyList<AdapterCommandDefinition>? commands, IReadOnlyList<ProviderCapability>? capabilities = null) =>
        new("demo", "Demo", "A demo adapter.", 1, [], capabilities ?? (commands is { Count: > 0 } ? Both : []), null, 0, commands);

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static int Problems(AdapterDescriptor descriptor) => AdapterCommandDefinitionValidator.Validate(descriptor).Count;

    public static void Run()
    {
        Expect(Problems(Adapter(null)) == 0 && Problems(Adapter([])) == 0, "An adapter with no commands is valid.");
        Expect(Problems(Adapter([Command(), Command("pump-running", kind: AdapterCommandValueKind.OnOff, unit: null)])) == 0, "Valid commands were refused.");
        Expect(Problems(Adapter([Command(verification: AdapterCommandVerification.AcknowledgementOnly, readback: null)],
            [new(ProviderCapabilityKind.Command, "Governed commands")])) == 0, "An acknowledgement-only command needs no Verification capability.");

        // The Command capability is advertised exactly when commands are defined.
        Expect(Problems(Adapter([Command()], [new(ProviderCapabilityKind.Verification, "Command verification")])) > 0, "Commands without the capability were accepted.");
        Expect(Problems(Adapter(null, Both)) > 0, "The capability without commands was accepted.");
        Expect(Problems(Adapter([Command()], [new(ProviderCapabilityKind.Command, "Governed commands")])) > 0, "A read-back command without the Verification capability was accepted.");

        foreach (var bad in new[]
        {
            Command(""), Command("has space"), Command("ADVISORY_REVIEW"), Command("advisory_review"),
            Command(readback: null), Command(verification: AdapterCommandVerification.AcknowledgementOnly, readback: "Speed"),
            Command(settle: 31), Command(settle: -1), Command(deadline: 5, settle: 5), Command(maxDeadline: 30, maxSettle: 30),
            Command(deadline: 0), Command(deadline: 301), Command(kind: AdapterCommandValueKind.OnOff, unit: "%"),
            Command(kind: (AdapterCommandValueKind)9), Command(verification: (AdapterCommandVerification)9),
            Command(target: [new("a", "A", "a", AdapterSettingKind.Text, true), new("a", "A2", "a", AdapterSettingKind.Text, true)])
        })
            Expect(Problems(Adapter([bad])) > 0, $"An invalid command was accepted: {bad.Name}/{bad.Verification}/{bad.DefaultSettleSeconds}.");
        Expect(Problems(Adapter([Command("Set-Speed"), Command("set-speed")])) > 0, "Names differing only by case were accepted.");
        Expect(Problems(Adapter(Enumerable.Range(0, 65).Select(index => Command($"c{index}")).ToArray())) > 0, "More than 64 commands were accepted.");

        // The descriptor travels Gateway -> Server as JSON; commands must survive it, and an older Gateway without them must still read.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var original = Adapter([Command()]);
        var copy = JsonSerializer.Deserialize<AdapterDescriptor>(JsonSerializer.Serialize(original, options), options)!;
        Expect(copy.Commands is { Count: 1 } && copy.Commands[0] == (original.Commands![0] with { TargetFields = copy.Commands[0].TargetFields }) &&
            copy.Commands[0].TargetFields.Single().Key == "setpoint" && copy.Commands[0].Verification == AdapterCommandVerification.ReadBack &&
            Problems(copy) == 0, "A command definition changed in transit.");
        var older = JsonSerializer.Deserialize<AdapterDescriptor>(
            """{"key":"old","displayName":"Old","description":"d","settingsVersion":1,"fields":[],"capabilities":[]}""", options)!;
        Expect(older.Commands is null && Problems(older) == 0, "A descriptor without commands must still be readable.");
    }
}
