using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Control;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.Connections;
using WellSiteAutoPilot.Persistence.Control;

/// <summary>
/// A command goes where the asset's own connection and the connection's catalog say, and every doubt is a stated
/// refusal. Nothing from a module or a policy can choose the connection, the provider-side id or the command spelling.
/// </summary>
internal static class ControlTargetResolverChecks
{
    public static async Task RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options, Guid assetTypeId)
    {
        var now = DateTimeOffset.UtcNow;
        var tag = Guid.NewGuid().ToString("N");
        var connection = Guid.NewGuid();
        var disabledConnection = Guid.NewGuid();
        var badSettingsConnection = Guid.NewGuid();
        var assets = Enumerable.Range(0, 9).Select(_ => Guid.NewGuid()).ToArray();
        var (plain, imported, disagree, noOwner, offline, inactive, unbound, badSettings, twoIds) =
            (assets[0], assets[1], assets[2], assets[3], assets[4], assets[5], assets[6], assets[7], assets[8]);

        await using (var setup = new WellSiteAutoPilotDbContext(options))
        {
            foreach (var (id, enabled, settings) in new[]
            {
                (connection, true, """{"baseUrl":"http://127.0.0.1:5091"}"""),
                (disabledConnection, false, "{}"),
                (badSettingsConnection, true, "not json")
            })
                setup.Connections.Add(new ConnectionEntity
                {
                    Id = id, Name = $"target-{tag}-{id:N}", NormalizedName = $"TARGET-{tag}-{id:N}".ToUpperInvariant(),
                    AdapterKey = "simulator", SettingsVersion = 1, SettingsJson = settings, IsEnabled = enabled,
                    Revision = 3, CreatedAtUtc = now, UpdatedAtUtc = now
                });
            await setup.SaveChangesAsync();

            AssetEntity Asset(Guid id, Guid? owner, bool active = true) => new()
            {
                Id = id, AssetTypeId = assetTypeId, Name = $"target-{tag}-{id:N}", IsActive = active, CreatedAtUtc = now,
                OwnerConnectionId = owner
            };
            setup.Assets.AddRange(Asset(plain, connection), Asset(imported, connection), Asset(disagree, connection),
                Asset(noOwner, null), Asset(offline, disabledConnection), Asset(inactive, connection, active: false),
                Asset(unbound, connection), Asset(badSettings, badSettingsConnection), Asset(twoIds, connection));
            await setup.SaveChangesAsync();

            SourceBindingEntity Binding(Guid asset, string externalId) => new()
            {
                Id = Guid.NewGuid(), ConnectionId = connection, AssetId = asset, ResourceKind = "simulator.asset",
                ExternalId = externalId, CreatedAtUtc = now, SnapshotJson = "{}"
            };
            setup.SourceBindings.AddRange(Binding(imported, "WELL-101"), Binding(disagree, "WELL-303"),
                Binding(twoIds, "WELL-A"), Binding(twoIds, "WELL-B"));

            foreach (var id in new[] { connection, badSettingsConnection })
            {
                setup.ConnectionCommandCatalogs.Add(new ConnectionCommandCatalogEntity
                {
                    ConnectionId = id, Version = 4, ChangedBy = "fixture", ChangedAtUtc = now
                });
            }
            await setup.SaveChangesAsync();
            setup.ConnectionCommandCatalogEntries.AddRange(
                new ConnectionCommandCatalogEntryEntity
                {
                    ConnectionId = connection, Command = "SET_SPEED", Enabled = true, Minimum = 0, Maximum = 12,
                    MaximumStep = 2, TargetJson = "{}", SettleSeconds = 2, DeadlineSeconds = 30
                },
                new ConnectionCommandCatalogEntryEntity
                {
                    ConnectionId = connection, Command = "PUMP_RUN", Enabled = false, TargetJson = "{}",
                    SettleSeconds = 2, DeadlineSeconds = 30
                },
                new ConnectionCommandCatalogEntryEntity
                {
                    ConnectionId = badSettingsConnection, Command = "SET_SPEED", Enabled = true, Minimum = 0, Maximum = 12,
                    TargetJson = "{}", SettleSeconds = 2, DeadlineSeconds = 30
                });
            await setup.SaveChangesAsync();
        }

        ConfiguredLogicDataBinding Data(Guid asset, string externalId, string provider = "simulator") =>
            new("value", asset, provider, externalId, "{}");

        async Task<ControlTargetResolution> ResolveAsync(Guid asset, string command, params ConfiguredLogicDataBinding[] bindings)
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            return await new ControlTargetResolver(db, new ConnectionCommandCatalogRepository(db))
                .ResolveAsync(asset, command, bindings);
        }

        static void Refused(ControlTargetResolution result, string code, string what)
        {
            if (result.Target is not null || result.RefusalCode != code)
                throw new InvalidOperationException($"{what}: expected {code} but got {result.RefusalCode ?? "a target"}.");
        }

        // The engineer's data binding supplies the id when the asset has no imported source binding.
        var viaData = await ResolveAsync(plain, "SET_SPEED", Data(plain, "WELL-101"));
        if (viaData.Target is not { } target || target.ConnectionId != connection || target.ConnectionRevision != 3 ||
            target.AdapterKey != "simulator" || target.ExternalId != "WELL-101" || target.CatalogVersion != 4 ||
            target.Entry.Command != "SET_SPEED" || target.Entry.Maximum != 12 ||
            target.Settings["baseUrl"] != "http://127.0.0.1:5091")
            throw new InvalidOperationException("A bound asset must resolve to its own connection, id and catalog entry.");

        // An imported source binding needs no data binding, and a catalog spelling still finds the entry.
        if ((await ResolveAsync(imported, "set_speed")).Target is not { ExternalId: "WELL-101", Entry.Command: "SET_SPEED" })
            throw new InvalidOperationException("An imported source binding must resolve, and the catalog's spelling must win.");
        if ((await ResolveAsync(imported, "SET_SPEED", Data(imported, "WELL-101"))).Target is null)
            throw new InvalidOperationException("Agreeing source and data bindings must resolve.");

        Refused(await ResolveAsync(disagree, "SET_SPEED", Data(disagree, "WELL-202")), GateCodes.TargetAmbiguous, "bindings that disagree");
        Refused(await ResolveAsync(twoIds, "SET_SPEED"), GateCodes.TargetAmbiguous, "two imported ids");
        Refused(await ResolveAsync(unbound, "SET_SPEED"), GateCodes.TargetNotBound, "no binding at all");
        Refused(await ResolveAsync(unbound, "SET_SPEED", Data(unbound, "WELL-101", provider: "foresite")), GateCodes.TargetNotBound,
            "a data binding for another provider");
        Refused(await ResolveAsync(unbound, "SET_SPEED", Data(plain, "WELL-101")), GateCodes.TargetNotBound,
            "a data binding for another asset");
        Refused(await ResolveAsync(noOwner, "SET_SPEED", Data(noOwner, "WELL-101")), GateCodes.TargetNoConnection, "no owner connection");
        Refused(await ResolveAsync(inactive, "SET_SPEED", Data(inactive, "WELL-101")), GateCodes.TargetNoConnection, "an inactive asset");
        Refused(await ResolveAsync(Guid.NewGuid(), "SET_SPEED"), GateCodes.TargetNoConnection, "an unknown asset");
        Refused(await ResolveAsync(offline, "SET_SPEED", Data(offline, "WELL-101")), GateCodes.TargetConnectionDisabled, "a disabled connection");
        Refused(await ResolveAsync(badSettings, "SET_SPEED", Data(badSettings, "WELL-101")), GateCodes.TargetConnectionDisabled,
            "settings that cannot be read");
        Refused(await ResolveAsync(plain, "NOT_LISTED", Data(plain, "WELL-101")), GateCodes.CommandNotInCatalog, "a command the catalog lacks");
        Refused(await ResolveAsync(Guid.Empty, "SET_SPEED"), GateCodes.TargetNoConnection, "an empty asset id");
        Refused(await ResolveAsync(plain, " ", Data(plain, "WELL-101")), GateCodes.TargetNoConnection, "an empty command");

        // A disabled entry resolves (it is the gate's catalog check that refuses it), so the refusal names the cause.
        if ((await ResolveAsync(plain, "PUMP_RUN", Data(plain, "WELL-101"))).Target is not { Entry.Enabled: false })
            throw new InvalidOperationException("A disabled catalog entry must resolve so the gate can name it.");
    }
}
