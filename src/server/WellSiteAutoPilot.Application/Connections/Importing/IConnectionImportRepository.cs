using System.Text.Json;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Application.Connections.Importing;

/// <summary>Everything the write needs, already decided: the plan to apply and the types to create Assets with.</summary>
/// <param name="ExpectedConnectionRevision">The connection revision the plan was made from; a changed connection is refused.</param>
public sealed record ImportApplyRequest(Guid ConnectionId, ImportPlan Plan, Guid WellTypeId, Guid GroupTypeId,
    int ExpectedConnectionRevision, string ExpectedHierarchyFingerprint, bool AdoptExistingHierarchy,
    IReadOnlyList<DiscoveryField> Fields, IReadOnlyList<string>? UnavailableFields, string? ActingAs);

public sealed record ImportApplyResult(int WellsCreated, int GroupsCreated, int GroupsReused, bool HierarchyAdopted = false);

public interface IConnectionImportRepository
{
    Task<ImportHierarchyStatus> ReadHierarchyStatusAsync(Guid connectionId, CancellationToken cancellationToken = default);

    Task<ImportHierarchyState> ReadHierarchyAsync(Guid connectionId, CancellationToken cancellationToken = default);

    /// <summary>The objects of this connection that already have an Asset, as <see cref="BindingKey"/> values.</summary>
    Task<IReadOnlySet<string>> BoundKeysAsync(Guid connectionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the groups, the Assets and their bindings in one transaction, or nothing. Needs permission to manage
    /// every Asset; only verified stable group identities are reused.
    /// </summary>
    Task<ImportApplyResult> ApplyAuthorizedAsync(ImportApplyRequest request, AccessSubject subject,
        SecurityActorContext actor, CancellationToken cancellationToken = default);

    static string BindingKey(string kind, string externalId) => JsonSerializer.Serialize(new[] { kind, externalId });
}
