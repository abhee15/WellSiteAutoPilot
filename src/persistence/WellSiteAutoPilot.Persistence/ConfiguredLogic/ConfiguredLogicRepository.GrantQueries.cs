using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Persistence.Security;

namespace WellSiteAutoPilot.Persistence.ConfiguredLogic;

public sealed partial class ConfiguredLogicRepository
{
    public async Task<ConfiguredLogicDefinition?> GetAuthorizedAsync(AccessSubject subject, Guid id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        await using var transaction = await BeginAuthorizedReadAsync(cancellationToken);
        var entity = await ScopedAccessQueries.ConfiguredLogic(dbContext, subject.UserId, WellSitePermissions.ConfiguredLogicRead,
            subject.WindowsGroups, (clock ?? TimeProvider.System).GetUtcNow())
            .SingleOrDefaultAsync(definition => definition.Id == id, cancellationToken);
        var result = entity is null ? null : ToDomain(entity, await LoadRevisionsAsync(id, cancellationToken));
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<IReadOnlyCollection<ConfiguredLogicDefinition>> ListAuthorizedAsync(AccessSubject subject,
        int limit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var transaction = await BeginAuthorizedReadAsync(cancellationToken);
        var entities = await ScopedAccessQueries.ConfiguredLogic(dbContext, subject.UserId, WellSitePermissions.ConfiguredLogicRead,
            subject.WindowsGroups, (clock ?? TimeProvider.System).GetUtcNow())
            .OrderBy(definition => definition.Name).ThenBy(definition => definition.Id).Take(limit).ToArrayAsync(cancellationToken);
        var result = new List<ConfiguredLogicDefinition>(entities.Length);
        foreach (var entity in entities)
            result.Add(ToDomain(entity, await LoadRevisionsAsync(entity.Id, cancellationToken)));
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private async Task<IDbContextTransaction?> BeginAuthorizedReadAsync(CancellationToken cancellationToken)
    {
        // Scope filtering and later revision/binding hydration must observe
        // the same snapshot; a new cross-scope revision cannot slip between.
        if (dbContext.Database.CurrentTransaction is not { } current)
            return await dbContext.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        if (current.GetDbTransaction().IsolationLevel is not (IsolationLevel.RepeatableRead or IsolationLevel.Serializable))
            throw new InvalidOperationException("Authorized configuration reads require a stable database snapshot.");
        return null;
    }
}
