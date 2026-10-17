namespace WellSiteAutoPilot.Application.Security;

/// <summary>
/// Current, resource-specific action prerequisites for an authorized page.
/// These hints never replace mutation authorization or business-state checks.
/// Missing rows mean no current read access, not unrestricted capabilities.
/// </summary>
public sealed record ConfiguredLogicActions(Guid Id, bool CanManage, bool CanRequestShadow);
public sealed record RecommendationActions(Guid Id, bool CanDecide);

public interface IResourceActionRepository
{
    Task<IReadOnlyCollection<ConfiguredLogicActions>> GetConfiguredLogicActionsAsync(AccessSubject subject,
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<RecommendationActions>> GetRecommendationActionsAsync(AccessSubject subject,
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}
