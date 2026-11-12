namespace WellSiteAutoPilot.Integration.Contracts.Providers;

// Server -> Gateway only. The Server supplies the saved connection; the Gateway never stores one.
public sealed record ResolveRequest(
    Guid ConnectionId, int ConnectionRevision, Dictionary<string, string?> Settings, string ExternalId, string Input,
    ResolveBasis? Basis = null, bool Read = true);

/// <summary>One input resolved on one asset, with a sample value when it resolved to a single source.</summary>
public sealed record ResolvePreview(ResolveResult Result, EngineeringValue? Value, string? ReadProblem);

public sealed record SetPointPlanRequest(
    Guid ConnectionId, int ConnectionRevision, Dictionary<string, string?> Settings, string ExternalId, string SetPoint, decimal Delta);

/// <summary>A dry-run write: what would be sent to the controller, with the before and after values. Nothing is sent.</summary>
public sealed record SetPointWritePlan(
    string WellId, string SetPoint, string Description, string Unit, decimal Current, decimal Proposed,
    decimal? RangeLow, decimal? RangeHigh, bool Allowed, string? Refusal);
