using System.Text.Json;
using System.Text.Json.Serialization;
using WellSiteAutoPilot.Api.Contracts.Serialization;

namespace WellSiteAutoPilot.Api.Contracts.Authoring;

// Closed wire vocabularies. Validation, test and build are independent states.
[JsonConverter(typeof(ClosedStringEnumConverter<AuthoringValidationState>))]
public enum AuthoringValidationState
{
    NotRun = 1,
    Valid = 2,
    Invalid = 3,
    Stale = 4
}

[JsonConverter(typeof(ClosedStringEnumConverter<AuthoringTestState>))]
public enum AuthoringTestState
{
    NotRun = 1,
    Passed = 2,
    Failed = 3,
    Stale = 4,
    Cancelled = 5
}

[JsonConverter(typeof(ClosedStringEnumConverter<AuthoringBuildState>))]
public enum AuthoringBuildState
{
    None = 1,
    Building = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5,
    Stale = 6
}

[JsonConverter(typeof(ClosedStringEnumConverter<AuthoringIssueSeverity>))]
public enum AuthoringIssueSeverity
{
    Error = 1,
    Warning = 2,
    Info = 3
}

public sealed record AuthoringStarterResponse(string Id, string Label, string Description);

/// <summary>Always readable; tells the client whether the local-only authoring feature is switched on.</summary>
public sealed record AuthoringCapabilitiesResponse(
    bool LocalBuildEnabled,
    bool TrustedLocalMode,
    string SdkVersion,
    string DocumentSchema,
    string EvaluationProfile,
    IReadOnlyList<AuthoringStarterResponse> Starters,
    IReadOnlyList<string> Operations,
    IReadOnlyList<string> Comparators);

/// <summary>Supply either a starter id or a full document; a bare request creates the minimal starter.</summary>
public sealed record CreateAuthoringDraftRequest(string? StarterId, JsonElement? Document);

public sealed record UpdateAuthoringDraftRequest(int ExpectedRevision, JsonElement Document);

public sealed record AuthoringIssueResponse(AuthoringIssueSeverity Severity, string Code, string Path, string Message);

public sealed record AuthoringBuildResponse(
    Guid BuildId,
    int Revision,
    AuthoringBuildState State,
    IReadOnlyList<AuthoringIssueResponse> Diagnostics,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? PackageSha256,
    long PackageBytes,
    Guid? CatalogModuleRecordId);

public sealed record AuthoringDraftSummaryResponse(
    Guid Id,
    int Revision,
    string ModuleId,
    string Version,
    string DisplayName,
    AuthoringValidationState ValidationState,
    AuthoringTestState TestState,
    AuthoringBuildState BuildState,
    DateTimeOffset UpdatedAt);

public sealed record AuthoringTestValueResponse(string Id, JsonElement Value, string? Unit);

public sealed record AuthoringTestCaseResponse(
    string FixtureId,
    string Label,
    bool Passed,
    string Outcome,
    string? FailureCode,
    string? Message,
    IReadOnlyList<AuthoringTestValueResponse> Outputs,
    IReadOnlyList<AuthoringTestValueResponse> Intermediates,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> Mismatches);

/// <summary>The last test run, pinned to a draft revision and the built package it exercised.</summary>
public sealed record AuthoringTestResponse(
    Guid TestId,
    int Revision,
    Guid BuildId,
    string PackageSha256,
    AuthoringTestState State,
    IReadOnlyList<AuthoringTestCaseResponse> Cases,
    IReadOnlyList<AuthoringIssueResponse> Diagnostics,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);

public sealed record AuthoringDraftResponse(
    Guid Id,
    int Revision,
    string ContentDigest,
    JsonElement Document,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string CreatedBy,
    AuthoringValidationState ValidationState,
    int? ValidatedRevision,
    IReadOnlyList<AuthoringIssueResponse> ValidationIssues,
    AuthoringTestState TestState,
    AuthoringBuildState BuildState,
    AuthoringBuildResponse? Build,
    AuthoringTestResponse? Test = null);
