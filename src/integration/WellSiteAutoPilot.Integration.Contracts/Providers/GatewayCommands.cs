using System.Text.Json.Serialization;

namespace WellSiteAutoPilot.Integration.Contracts.Providers;

/// <summary>
/// The Gateway's closed answer to a command. Anything that is not one of these three (a timeout, a transport error, an
/// unreadable body, a server error) is <b>ambiguous</b>: the provider may or may not have acted, and the caller must
/// never retry it.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GatewayCommandOutcome
{
    /// <summary>The provider took the command. This is not proof that the value changed.</summary>
    Sent = 0,

    /// <summary>The provider definitely did not act, and said why in <see cref="GatewayCommandResponse.Code"/>.</summary>
    Rejected = 1,

    /// <summary>Nothing was sent, by request.</summary>
    DryRun = 2
}

/// <summary>Server -> Gateway only; never a worker or module request. The target comes from the site's catalog, never a module.</summary>
public sealed record GatewayCommandRequest(
    Guid ConnectionId,
    int ConnectionRevision,
    Dictionary<string, string?> Settings,
    string ExternalId,
    string Command,
    string RequestId,
    decimal Value,
    string? Unit,
    Dictionary<string, string?> Target);

public sealed record GatewayCommandResponse(GatewayCommandOutcome Outcome, string? Code, string? Reference);
