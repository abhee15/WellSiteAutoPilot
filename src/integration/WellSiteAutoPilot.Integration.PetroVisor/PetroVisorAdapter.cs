using System.Net.Http;
using System.Security.Authentication;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.PetroVisor;

/// <summary>
/// Minimal PetroVisor workspace inventory: no source hierarchy, signal catalog,
/// ingestion, or SCADA control. It never changes PetroVisor data.
/// SDK session/authentication details remain isolated behind the reader seam.
/// </summary>
public static class PetroVisorAdapter
{
    public const string Key = "petrovisor";
    public const string WorkspaceSetting = "workspace";
    public const string EndpointSetting = "baseUrl";
    public const string ResourceKind = "petrovisor.entity";

    public static AdapterDescriptor Descriptor { get; } = new(
        Key, "PetroVisor",
        "PetroVisor workspace entities using the Integration Gateway Windows service identity. " +
        "POC: connection discovery requires a validated PetroVisor SDK Windows SSO session factory.",
        1,
        [
            new AdapterSettingField(EndpointSetting, "PetroVisor address",
                "The approved PetroVisor Web API endpoint. The SDK authentication endpoint mapping must be tested.",
                AdapterSettingKind.Url, true),
            new AdapterSettingField(WorkspaceSetting, "Workspace",
                "Exact PetroVisor workspace name. Each saved connection is bound to one workspace.",
                AdapterSettingKind.Text, true)
        ],
        [new ProviderCapability(ProviderCapabilityKind.AssetDiscovery, "Workspace entities")],
        [new DiscoveryField("entityType", "Entity type", "Unspecified type")]);

    public static async Task<DiscoveryResult> DiscoverAsync(
        IPetroVisorEntityReader reader,
        string workspace,
        string actingAs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(actingAs);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var entities = await reader.ReadEntitiesAsync(workspace, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return MapEntities(workspace, entities, actingAs);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Incomplete("PetroVisor did not answer before the connection timed out.", actingAs);
        }
        catch (UnauthorizedAccessException)
        {
            return Incomplete("The Gateway Windows account is not permitted to read this PetroVisor workspace.", actingAs);
        }
        catch (AuthenticationException)
        {
            return Incomplete("PetroVisor did not accept the Gateway Windows account.", actingAs);
        }
        catch (HttpRequestException)
        {
            return Incomplete("PetroVisor could not be reached or refused the connection.", actingAs);
        }
        catch (Exception)
        {
            // Do not expose vendor error responses, credentials, paths, or stack traces through discovery.
            return Incomplete("PetroVisor entity discovery failed. Check the Gateway logs.", actingAs);
        }
    }

    public static DiscoveryResult MapEntities(
        string workspace,
        IReadOnlyCollection<PetroVisorEntitySnapshot> entities,
        string actingAs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentException.ThrowIfNullOrWhiteSpace(actingAs);

        // Incomplete must never mean 'all assets disappeared'. Missing/duplicate source
        // identities are a hard stop, not an invitation to delete/re-import bindings.
        if (entities.Any(item => item is null || string.IsNullOrWhiteSpace(item.Name)))
            return Incomplete("Some PetroVisor entities had no usable identity. Nothing was imported.", actingAs);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in entities)
        {
            if (!names.Add(entity.Name))
                return Incomplete("PetroVisor returned duplicate entity identities. Nothing was imported.", actingAs);
        }

        var mapped = entities
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item =>
            {
                var facts = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["workspace"] = workspace,
                    ["entityName"] = item.Name
                };
                if (!string.IsNullOrWhiteSpace(item.EntityType))
                    facts["entityType"] = item.EntityType;
                if (!string.IsNullOrWhiteSpace(item.Alias))
                    facts["alias"] = item.Alias;

                // ExternalId is unique in the selected workspace. The Connection is
                // workspace-bound, and the import key is Connection + Kind + ExternalId.
                return new DiscoveredAsset(item.Name, item.Name, ResourceKind, null, facts);
            }).ToArray();

        return new DiscoveryResult(true, mapped, [], actingAs);
    }

    private static DiscoveryResult Incomplete(string reason, string actingAs) =>
        new(false, [], [reason], actingAs);
}

/// <summary>A PetroVisor Entity: classification is not a source-parent relationship.</summary>
public sealed record PetroVisorEntitySnapshot(string Name, string? EntityType, string? Alias = null);

/// <summary>Provider-specific SDK session/SSO factory lives behind this seam.</summary>
public interface IPetroVisorEntityReader
{
    Task<IReadOnlyCollection<PetroVisorEntitySnapshot>> ReadEntitiesAsync(
        string workspace, CancellationToken cancellationToken);
}
