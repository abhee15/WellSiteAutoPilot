using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Application.Authoring;

/// <summary>Resolves one imported asset and quantity to a Server-side read, applying the same checks as a Shadow read plan.</summary>
public interface IAuthoringPreviewSource
{
    Task<AuthorizedExecutionRead> ResolveAsync(AccessSubject subject, Guid assetId, string quantity,
        CancellationToken cancellationToken = default);
}

public sealed record AuthoringPreviewValue(Guid AssetId, string Quantity, decimal? Value, string Unit,
    DateTimeOffset TimestampUtc, string Quality, string Source, string AdapterKey);

/// <summary>
/// Reads a single current value so an author can see live data while editing. It never creates an execution,
/// a read plan or any stored record, and it never writes to the provider.
/// </summary>
public sealed class AuthoringPreviewService(AuthoringOptions options, IAuthoringPreviewSource source,
    IExecutionMeasurementGateway gateway)
{
    public async Task<AuthoringPreviewValue> PreviewAsync(AccessSubject subject, Guid assetId, string? quantity,
        CancellationToken cancellationToken = default)
    {
        if (!options.Enabled)
            throw new WellSiteAutoPilotException("AUTHORING_LOCAL_DISABLED", FailureKind.NotFound,
                "Local authoring is disabled. Set Authoring:LocalBuild:Enabled to true for local development.");
        if (assetId == Guid.Empty || string.IsNullOrWhiteSpace(quantity))
            throw new WellSiteAutoPilotException("AUTHORING_PREVIEW_INVALID", FailureKind.Validation,
                "Choose an asset and a quantity to preview.");
        var read = await source.ResolveAsync(subject, assetId, quantity.Trim(), cancellationToken);
        var value = await gateway.GetCurrentAsync(read, cancellationToken);
        return new(assetId, value.Quantity, value.Value, value.Unit, value.TimestampUtc, value.Quality, value.Source, read.AdapterKey);
    }
}
