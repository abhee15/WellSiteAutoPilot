using WellSiteAutoPilot.Application.Authoring;
using WellSiteAutoPilot.Application.Connections;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.ForeSite;

/// <summary>Adapter profiles are built only from what adapters declare, so they are checked without a Gateway.</summary>
internal static class AdapterProfileChecks
{
    public static void Verify()
    {
        foreach (var key in new[] { AdapterReadRules.ForeSiteKey, AdapterReadRules.CygNetKey })
        {
            var described = AdapterQuantities.For(key).Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
            var missing = AdapterReadRules.Find(key)!.CurrentQuantities.Where(name => !described.Contains(name)).ToArray();
            Check(missing.Length == 0, $"{key} describes every quantity it may read: missing {string.Join(",", missing)}");
            Check(AdapterQuantities.ReadBudget(key) is not null, $"{key} declares how long one read may take");
        }
        Check(AdapterQuantities.ForeSiteSurfaceCardsSchemaId == ForeSiteCards.SchemaId &&
              AdapterQuantities.ForeSiteSurfaceCards == ForeSiteCards.Quantity, "the surface card description matches the adapter");

        var cygnet = new AdapterDescriptor(AdapterReadRules.CygNetKey, "CygNet", "Facilities.", 1, [],
            [new(ProviderCapabilityKind.AssetDiscovery, "Facilities"), new(ProviderCapabilityKind.CurrentData, "Current point values")]);
        var listOnly = new AdapterDescriptor("petrovisor", "PetroVisor", "Entities.", 1, [],
            [new(ProviderCapabilityKind.AssetDiscovery, "Workspace entities")]);
        ConnectionRecord Connection(string adapter, bool enabled, int revision, string? outcome, int testedRevision = 1) =>
            new(Guid.NewGuid(), "c" + Guid.NewGuid(), adapter, 1, new Dictionary<string, string>(), enabled, revision,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
                outcome is null ? null : new(outcome, "m", null, DateTimeOffset.UnixEpoch, testedRevision));
        var connections = new[]
        {
            Connection("cygnet", true, 1, AdapterTestOutcomes.Healthy),
            Connection("cygnet", true, 2, AdapterTestOutcomes.Healthy),
            Connection("cygnet", true, 1, AdapterTestOutcomes.Unreachable),
            Connection("cygnet", false, 1, AdapterTestOutcomes.Healthy),
            Connection("foresite", true, 1, AdapterTestOutcomes.NeedsAttention)
        };
        var fractionInput = AuthoringStarters.Find(AuthoringStarters.ObserveInputId)!;
        var fraction = fractionInput with
        {
            Id = "fraction",
            Document = fractionInput.Document with { Inputs = [fractionInput.Document.Inputs[0] with { Unit = "1" }] }
        };
        var profiles = AdapterProfiles.Build([cygnet, ForeSiteAdapter.Descriptor, listOnly], connections,
            [.. AuthoringStarters.All, fraction]);

        var c = profiles[0];
        Check(c.CanRead && !c.CanWrite && c.ResourceKind == AdapterReadRules.CygNetResourceKind, "CygNet reads and never writes");
        Check(c.Quantities.Single(item => item.Name == "MotorAmps").NeedsMapping, "motor current must be mapped first");
        Check(c.Connections == new AdapterConnectionSummary(4, 3, 1, 0, 1, 1),
            "a disabled connection is not counted as checked and an old test does not count: " + c.Connections);
        Check(string.Join(",", c.Examples.Select(item => item.StarterId)) == "threshold-alert,observe-input",
            "starters whose inputs CygNet serves in the same unit are examples: " + string.Join(",", c.Examples.Select(item => item.StarterId)));
        Check(c.Limits.Any(item => item.Key == "readTimeout" && item.Value == "10 s") &&
              c.Limits.Any(item => item.Key == "discoveryObjects" && item.Value == "50,000") &&
              c.Limits.All(item => item.Key != "structuredValueSize"), "CygNet limits: " + string.Join(";", c.Limits));
        Check(c.MinimumCadenceSeconds is null, "no adapter declares a minimum cadence yet");

        var f = profiles[1];
        Check(f.Quantities.Single(item => item.Name == "SurfaceCards") is { ValueKind: AdapterValueKinds.Structured, Unit: null },
            "surface cards are structured");
        Check(f.Limits.Any(item => item.Key == "structuredValueSize" && item.Value == "64 KB"), "structured values have a size limit");
        Check(f.Examples.All(item => item.StarterId != AuthoringStarters.CardCalibrationId),
            "the card starter needs a calibration input ForeSite does not serve");

        var p = profiles[2];
        Check(!p.CanRead && p.Quantities.Count == 0 && p.Examples.Count == 0 && p.ResourceKind is null,
            "an adapter without read rules offers nothing to read");
        Check(p.Connections.Total == 0, "connections are counted per adapter");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Adapter profile check failed: " + message);
    }
}
