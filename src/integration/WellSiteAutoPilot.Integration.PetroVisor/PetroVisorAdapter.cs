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
            new AdapterSettingField(EndpointSetting, "PetroVisor identity server address",
                "The address of the PetroVisor identity server, for example https://identity.us1.petrovisor.com. " +
                "Not the web app or login page address; only the server part of what you enter is used.",
                AdapterSettingKind.Url, true),
            new AdapterSettingField(WorkspaceSetting, "Workspace",
                "Exact PetroVisor workspace name. Each saved connection is bound to one workspace.",
                AdapterSettingKind.Text, true),
            new AdapterSettingField(IdentitySetting, "Token address (optional)",
                "Leave empty: the Gateway asks the identity server where to sign in. Only fill this in if the sign-in " +
                "cannot be found automatically, with the full token address or the Keycloak realm address.",
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

    /// <summary>
    /// The identity server address as the SDK wants it. People paste whatever they have open in the browser (a login
    /// page, a page of the app), so only the server part (scheme, host and port) is kept.
    /// </summary>
    public static Uri DiscoveryAddress(string setting)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(setting);
        var given = new Uri(setting.Trim(), UriKind.Absolute);
        return new Uri(given.GetLeftPart(UriPartial.Authority), UriKind.Absolute);
    }

    /// <summary>
    /// A plain-words reason when the address is the PetroVisor web app rather than its identity server, else null.
    /// The web app is served from an "app." host; sign-in is not.
    /// </summary>
    public static string? AddressProblem(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return address.Host.StartsWith("app.", StringComparison.OrdinalIgnoreCase)
            ? "That is the PetroVisor web app address, not its identity server. Enter the identity server address, for example https://identity.us1.petrovisor.com."
            : null;
    }

    /// <summary>
    /// What PetroVisor itself said, made safe to show: one line, no control characters, long token-like words and
    /// addresses removed, and short. Used only when the failure has no better explanation.
    /// </summary>
    public static string SafeVendorReason(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var words = (exception.Message ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length < 40 && !word.Contains("://", StringComparison.Ordinal))
            .Select(word => new string(word.Where(character => !char.IsControl(character)).ToArray()));
        var text = string.Join(' ', words).Trim();
        if (text.Length == 0)
            return "PetroVisor gave no reason.";
        return text.Length <= 200 ? text : text[..200] + "...";
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
        catch (AuthenticationException exception)
        {
            return Incomplete("PetroVisor did not accept the Gateway's sign-in: " + SafeVendorReason(exception), actingAs);
        }
        catch (HttpRequestException exception)
        {
            return Incomplete("PetroVisor could not be reached or refused the connection: " + SafeVendorReason(exception), actingAs);
        }
        catch (Exception exception)
        {
            // The vendor's own reason (for example "user not found"), cleaned of anything token-like, so it can be acted on.
            return Incomplete("PetroVisor entity discovery failed: " + SafeVendorReason(exception), actingAs);
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
        catch (AuthenticationException exception)
        {
            return new(AdapterTestOutcomes.NeedsAttention,
                "PetroVisor did not accept the Gateway's sign-in: " + SafeVendorReason(exception), actingAs);
        }
        catch (HttpRequestException exception)
        {
            return new(AdapterTestOutcomes.Unreachable,
                "PetroVisor could not be reached or refused the connection: " + SafeVendorReason(exception), actingAs);
        }
        catch (Exception exception)
        {
            // The vendor's own reason (for example "user not found"), cleaned of anything token-like, so it can be acted on.
            return new(AdapterTestOutcomes.NeedsAttention, "The PetroVisor check failed: " + SafeVendorReason(exception), actingAs);
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
