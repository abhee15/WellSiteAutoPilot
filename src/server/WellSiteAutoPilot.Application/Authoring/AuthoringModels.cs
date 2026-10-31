namespace WellSiteAutoPilot.Application.Authoring;

/// <summary>Local proof-of-concept settings. Everything is off unless <see cref="Enabled"/> is set.</summary>
public sealed class AuthoringOptions
{
    public const string SectionName = "Authoring:LocalBuild";
    public bool Enabled { get; set; }
    public string? StorePath { get; set; }
    public string? WorkRoot { get; set; }
    public string? SdkProjectPath { get; set; }
    public int BuildTimeoutSeconds { get; set; } = 240;
    public string DotnetPath { get; set; } = "dotnet";
}

public enum AuthoringValidationState { NotRun = 1, Valid, Invalid, Stale }
public enum AuthoringTestState { NotRun = 1, Passed, Failed, Stale, Cancelled }
public enum AuthoringBuildState { None = 1, Building, Succeeded, Failed, Cancelled, Stale }

public sealed record AuthoringValidationRecord(
    int Revision, bool IsValid, IReadOnlyList<AuthoringIssue> Issues, DateTimeOffset ValidatedAt);

public sealed record AuthoringCatalogSubmission(Guid ModuleRecordId, DateTimeOffset SubmittedAt);

public sealed record AuthoringBuildRecord(
    Guid BuildId, int Revision, string DocumentDigest, AuthoringBuildState State,
    IReadOnlyList<AuthoringIssue> Diagnostics, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt = null,
    string? PackageSha256 = null, long PackageBytes = 0, AuthoringCatalogSubmission? Catalog = null);

public sealed record AuthoringDraftRecord(
    Guid Id, int Revision, string ContentDigest, AuthoringDocument Document,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string CreatedBy,
    AuthoringValidationRecord? Validation = null, AuthoringBuildRecord? Build = null);

public sealed record AuthoringDraftView(
    AuthoringDraftRecord Draft, AuthoringValidationState Validation, AuthoringTestState Test,
    AuthoringBuildState Build, AuthoringBuildRecord? EffectiveBuild);

public sealed record AuthoringBuilderResult(bool Success, byte[]? Package, IReadOnlyList<AuthoringIssue> Diagnostics);

public interface IAuthoringDraftStore
{
    Task<IReadOnlyList<AuthoringDraftRecord>> ListAsync(CancellationToken cancellationToken);
    Task<AuthoringDraftRecord?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task CreateAsync(AuthoringDraftRecord record, CancellationToken cancellationToken);
    /// <summary>Atomic read-modify-write; null when the draft does not exist.</summary>
    Task<AuthoringDraftRecord?> UpdateAsync(
        Guid id, Func<AuthoringDraftRecord, AuthoringDraftRecord> mutate, CancellationToken cancellationToken);
    Task SavePackageAsync(Guid draftId, Guid buildId, byte[] package, CancellationToken cancellationToken);
    Task<byte[]?> ReadPackageAsync(Guid draftId, Guid buildId, CancellationToken cancellationToken);
}

public interface IAuthoringBuilder
{
    Task<AuthoringBuilderResult> BuildAsync(AuthoringGeneratedProject project, CancellationToken cancellationToken);
}
