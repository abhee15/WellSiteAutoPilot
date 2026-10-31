using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;

// Manifest version 3 named provider roles and configured-logic role bindings.
internal static class ProviderDependencyChecks
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };

    public static async Task RunAsync()
    {
        var shape = new LogicValueSchema("example.number/v1", JsonSerializer.Deserialize<JsonElement>("""{"type":"number"}"""));
        var manifest = new LogicModuleManifest(3, "example.wami-analysis", "1.0.0", "WAMI analysis", "Test",
            LogicRuntimeKind.DotNet, ExecutionProfile.ScheduledOneShot, "Example.Module", "1.2.0",
            [new("well", 1, 1, null, [])],
            [new("whp", "well", "WellheadPressure", "Current", "psia", 60, false, shape, "structured.current")], [],
            OutputRequirements: [new("rate", "LiquidRate", "stb/d", shape)],
            ProviderDependencies:
            [
                new("data", LogicProviderDependencyKind.Data, "foresite"),
                new("model", LogicProviderDependencyKind.Model, "foresite"),
                new("calc", LogicProviderDependencyKind.Calculation, "wami", ["nodal-analysis"])
            ]);

        await LogicModuleManifestValidator.ValidateSchemasAsync(manifest);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, Options)));
        var reread = await LogicModuleManifestReader.ReadAsync(stream);
        Require(reread.ProviderDependencies!.Count == 3 && reread.ProviderDependencies.Last().Functions!.Single() == "nodal-analysis" &&
                reread.ProviderDependencies.Last().Kind == LogicProviderDependencyKind.Calculation, "v3 dependencies round-trip through the reader");

        // Existing versions are untouched: a V2 manifest with RequiredAdapterKey stays valid, and may not declare roles.
        var v2 = manifest with { ManifestVersion = 2, ProviderDependencies = null, RequiredAdapterKey = "foresite" };
        await LogicModuleManifestValidator.ValidateSchemasAsync(v2);
        var dependency = manifest.ProviderDependencies!.First();
        foreach (var (invalid, code) in new[]
        {
            (manifest with { ManifestVersion = 2 }, "MODULE_CONTRACT_INVALID"),
            (manifest with { RequiredAdapterKey = "foresite" }, "MODULE_CONTRACT_INVALID"),
            (manifest with { ProviderDependencies = null }, "MODULE_PROVIDER_DEPENDENCY_INVALID"),
            (manifest with { ProviderDependencies = [] }, "MODULE_PROVIDER_DEPENDENCY_INVALID"),
            (manifest with { ProviderDependencies = [dependency, dependency with { Role = "DATA" }] }, "MODULE_PROVIDER_ROLE_DUPLICATE"),
            (manifest with { ProviderDependencies = [dependency with { Role = "bad role" }] }, "MODULE_IDENTIFIER_INVALID"),
            (manifest with { ProviderDependencies = [dependency with { AdapterKey = "" }] }, FailureCodes.ValidationFailed),
            (manifest with { ProviderDependencies = [dependency with { Kind = (LogicProviderDependencyKind)9 }] }, "MODULE_PROVIDER_DEPENDENCY_INVALID"),
            (manifest with { ProviderDependencies = [dependency with { Functions = ["nodal-analysis"] }] }, "MODULE_PROVIDER_DEPENDENCY_INVALID"),
            (manifest with { ProviderDependencies = [dependency with { Kind = LogicProviderDependencyKind.Calculation, Functions = ["a", "A"] }] }, "MODULE_PROVIDER_DEPENDENCY_INVALID")
        }) await Reject(() => LogicModuleManifestValidator.ValidateSchemasAsync(invalid), code);

        var foresite = new ProviderConnectionInfo(Guid.NewGuid(), "foresite", true);
        var other = new ProviderConnectionInfo(Guid.NewGuid(), "foresite", true);
        var wami = new ProviderConnectionInfo(Guid.NewGuid(), "wami", true);
        var connections = new[] { foresite, other, wami }.ToDictionary(c => c.Id);
        ProviderConnectionInfo? Find(Guid id) => connections.GetValueOrDefault(id);
        ProviderRoleBinding[] Good() => [new("data", foresite.Id), new("model", other.Id), new("calc", wami.Id)];

        ProviderRoleBindingValidator.Validate(manifest, Good(), Find);
        ProviderRoleBindingValidator.Validate(v2, [], Find);

        foreach (var (bindings, code) in new (ProviderRoleBinding[], string)[]
        {
            (Good()[..2], "PROVIDER_ROLE_UNBOUND"),
            ([.. Good(), new("extra", wami.Id)], "PROVIDER_ROLE_UNKNOWN"),
            ([.. Good(), new("data", other.Id)], "PROVIDER_ROLE_DUPLICATE"),
            ([new("data", foresite.Id), new("model", other.Id), new("calc", Guid.NewGuid())], "PROVIDER_CONNECTION_NOT_FOUND"),
            ([new("data", foresite.Id), new("model", other.Id), new("calc", foresite.Id)], "PROVIDER_ADAPTER_MISMATCH"),
            ([new("data", wami.Id), new("model", other.Id), new("calc", wami.Id)], "PROVIDER_ADAPTER_MISMATCH")
        })
            Rejects(() => ProviderRoleBindingValidator.Validate(manifest, bindings, Find), code);
        connections[foresite.Id] = foresite with { Enabled = false };
        Rejects(() => ProviderRoleBindingValidator.Validate(manifest, Good(), Find), "PROVIDER_CONNECTION_DISABLED");
        Rejects(() => ProviderRoleBindingValidator.Validate(v2, [new("data", wami.Id)], Find), "PROVIDER_ROLE_UNKNOWN");
        Console.WriteLine("Provider dependency (manifest v3) and role binding checks passed.");
    }

    private static void Require(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("Failed: " + name);
    }

    private static void Rejects(Action action, string code)
    {
        try { action(); }
        catch (WellSiteAutoPilotException error) when (error.Code == code) { return; }
        throw new InvalidOperationException("Expected " + code);
    }

    private static async Task Reject(Func<Task> action, string code)
    {
        try { await action(); }
        catch (WellSiteAutoPilotException error) when (error.Code == code) { return; }
        throw new InvalidOperationException("Expected " + code);
    }
}
