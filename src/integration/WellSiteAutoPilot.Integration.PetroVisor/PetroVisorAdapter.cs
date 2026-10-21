using System.Net.Http;
using System.Security.Authentication;
using MyrConn.PetroVisorFramework.API.Authorization;
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
    public const string IdentitySetting = "identityUrl";
    public const string ResourceKind = "petrovisor.entity";

    public static AdapterDescriptor Descriptor { get; } = new(
        Key, "PetroVisor",
        "Entities held in one PetroVisor workspace. The Integration Gateway signs in to PetroVisor; " +
        "which identity it uses is set up on the Gateway computer, never in a connection.",
        1,
        [
            new AdapterSettingField(EndpointSetting, "PetroVisor address",
                "The PetroVisor discovery service address, for example https://petrovisor.example.com/discovery.",
                AdapterSettingKind.Url, true),
            new AdapterSettingField(WorkspaceSetting, "Workspace",
                "Exact PetroVisor workspace name. Each saved connection is bound to one workspace.",
                AdapterSettingKind.Text, true),
            new AdapterSettingField(IdentitySetting, "Identity provider address",
                "Where the Gateway signs in: the Keycloak realm address (for example https://login.example.com/realms/petrovisor) " +
                "or its token address. Leave empty to ask PetroVisor.",
                AdapterSettingKind.Url, false)
        ],
        [new ProviderCapability(ProviderCapabilityKind.AssetDiscovery, "Workspace entities")],
        [new DiscoveryField("entityType", "Entity type", "Unspecified type")]);

    /// <summary>The identity provider address from the connection, or null when it was left empty.</summary>
    public static Uri? IdentityAddress(IReadOnlyDictionary<string, string?> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.TryGetValue(IdentitySetting, out var value) && !string.IsNullOrWhiteSpace(value)
            ? new Uri(value.Trim(), UriKind.Absolute)
            : null;
    }

    /// <summary>The discovery service address as the SDK wants it: absolute, with the trailing slash dropped.</summary>
    public static Uri DiscoveryAddress(string setting)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(setting);
        return new Uri(setting.Trim().TrimEnd('/'), UriKind.Absolute);
    }

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
        catch (PetroVisorSignInUnavailableException exception)
        {
            return Incomplete(exception.Message, actingAs);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or AuthorizationException)
        {
            return Incomplete("The signed-in PetroVisor account is not permitted to read this workspace.", actingAs);
        }
        catch (AuthenticationException)
        {
            return Incomplete("PetroVisor did not accept the Gateway's sign-in.", actingAs);
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

    /// <summary>Tries the connection: signs in and reads the workspace's entity types. Failures are results, never exceptions.</summary>
    public static async Task<AdapterTestResult> CheckAsync(
        IPetroVisorEntityReader reader, string workspace, string actingAs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        try
        {
            var count = await reader.CountEntityTypesAsync(workspace, cancellationToken);
            return new(AdapterTestOutcomes.Healthy,
                $"PetroVisor answered and this account can read the workspace ({count} entity types).", actingAs);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new(AdapterTestOutcomes.Unreachable, "PetroVisor did not answer in time.", actingAs);
        }
        catch (PetroVisorSignInUnavailableException exception)
        {
            return new(AdapterTestOutcomes.NeedsAttention, exception.Message, actingAs);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or AuthorizationException)
        {
            return new(AdapterTestOutcomes.NeedsAttention,
                "The signed-in PetroVisor account is not permitted to read this workspace.", actingAs);
        }
        catch (AuthenticationException)
        {
            return new(AdapterTestOutcomes.NeedsAttention, "PetroVisor did not accept the Gateway's sign-in.", actingAs);
        }
        catch (HttpRequestException)
        {
            return new(AdapterTestOutcomes.Unreachable, "PetroVisor could not be reached or refused the connection.", actingAs);
        }
        catch (Exception)
        {
            // Vendor error text can carry addresses or tokens, so it is not passed on.
            return new(AdapterTestOutcomes.NeedsAttention, "The PetroVisor check failed. Check the Gateway logs.", actingAs);
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

    /// <summary>Signs in and counts the workspace's entity types: the cheapest read that proves access.</summary>
    Task<int> CountEntityTypesAsync(string workspace, CancellationToken cancellationToken);
}
