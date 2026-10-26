namespace WellSiteAutoPilot.Application.Logic;

// An internal durable work item, not a broker subject sent to workers.
public sealed record ModulePackagePublication(
    Guid LogicModuleId, string ModuleId, string ModuleVersion, string PackageSha256, string? CorrelationId, string? TraceParent = null)
{
    public const string OutboxType = "module-package.publication.v1";
}

public interface IModulePackageObjectStore
{
    Task PutAsync(string packageSha256, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default);
}
