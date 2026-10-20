using WellSiteAutoPilot.Application.Security;

namespace WellSiteAutoPilot.Application.Connections.Importing;

/// <summary>Everything the write needs, already decided: the plan to apply and the types to create Assets with.</summary>
public sealed record ImportApplyRequest(Guid ConnectionId, ImportPlan Plan, Guid WellTypeId, Guid GroupTypeId);

public sealed record ImportApplyResult(int WellsCreated, int GroupsCreated, int GroupsReused);

public interface IConnectionImportRepository
{
    /// <summary>The objects of this connection that already have an Asset, as <see cref="BindingKey"/> values.</summary>
    Task<IReadOnlySet<string>> BoundKeysAsync(Guid connectionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the groups, the Assets and their bindings in one transaction, or nothing. Needs permission to manage
    /// every Asset; groups that already exist under the same parent with the same name are reused.
    /// </summary>
    Task<ImportApplyResult> ApplyAuthorizedAsync(ImportApplyRequest request, AccessSubject subject,
        SecurityActorContext actor, CancellationToken cancellationToken = default);

    static string BindingKey(string kind, string externalId) => kind + "\u0001" + externalId;
}
