using WellSiteAutoPilot.Api.Contracts.Authoring;
using WellSiteAutoPilot.Application.Authoring;
using WellSiteAutoPilot.Server.Contracts;

namespace WellSiteAutoPilot.Server.Authoring;

internal static class AuthoringResponseMapping
{
    public static AuthoringCapabilitiesResponse ToCapabilities(AuthoringOptions options) => new(
        options.Enabled, true, AuthoringModuleGenerator.SdkVersion, AuthoringDocument.SchemaId,
        AuthoringDocument.DecimalProfile,
        [.. AuthoringStarters.All.Select(s => new AuthoringStarterResponse(s.Id, s.Label, s.Description))],
        [.. AuthoringOperations.Arity.Keys.Order(StringComparer.Ordinal)], AuthoringOperations.Comparators);

    public static AuthoringDraftSummaryResponse ToSummary(AuthoringDraftView view) => new(
        view.Draft.Id, view.Draft.Revision, view.Draft.Document.Module.ModuleId, view.Draft.Document.Module.Version,
        view.Draft.Document.Module.DisplayName, view.Validation.ToContract(), view.Test.ToContract(),
        view.Build.ToContract(), view.Draft.UpdatedAt);

    public static AuthoringDraftResponse ToResponse(AuthoringDraftView view)
    {
        var draft = view.Draft;
        return new(draft.Id, draft.Revision, draft.ContentDigest, AuthoringJson.ToElement(draft.Document),
            draft.CreatedAt, draft.UpdatedAt, draft.CreatedBy, view.Validation.ToContract(),
            draft.Validation?.Revision, [.. (draft.Validation?.Issues ?? []).Select(ToIssue)],
            view.Test.ToContract(), view.Build.ToContract(),
            view.EffectiveBuild is null ? null : ToBuild(view.EffectiveBuild));
    }

    private static AuthoringIssueResponse ToIssue(AuthoringIssue issue) =>
        new(issue.Severity.ToContract(), issue.Code, issue.Path, issue.Message);

    private static AuthoringBuildResponse ToBuild(AuthoringBuildRecord build) => new(
        build.BuildId, build.Revision, build.State.ToContract(), [.. build.Diagnostics.Select(ToIssue)],
        build.StartedAt, build.CompletedAt, build.PackageSha256, build.PackageBytes, build.Catalog?.ModuleRecordId);
}
