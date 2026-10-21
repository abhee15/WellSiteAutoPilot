using MyrConn.PetroVisorFramework.API;
using MyrConn.PetroVisorFramework.API.Entities;

namespace WellSiteAutoPilot.Integration.PetroVisor;

/// <summary>Reads entity names and types from a workspace through the SDK. Read-only: it never calls Save, Delete or Acquire.</summary>
public sealed class PetroVisorSdkEntityReader(IPetroVisorSessionFactory sessions, Uri discoveryServiceUrl, Uri? identityProvider = null) : IPetroVisorEntityReader
{
    public async Task<IReadOnlyCollection<PetroVisorEntitySnapshot>> ReadEntitiesAsync(
        string workspace, CancellationToken cancellationToken)
    {
        var provider = sessions.Create(discoveryServiceUrl, workspace, identityProvider);
        try
        {
            var service = provider.RepositoryServices.Get<Entity>();
            IEnumerable<Entity> items = service is IRepositoryServiceAsync<Entity> asynchronous
                ? await asynchronous.GetItemsAsync(cancellationToken)
                : await Task.Run(() => service.GetItems(cancellationToken), cancellationToken);
            return items.Select(item => new PetroVisorEntitySnapshot(item.Name, item.EntityTypeName, item.Alias)).ToArray();
        }
        finally
        {
            (provider as IDisposable)?.Dispose();
        }
    }

    public async Task<int> CountEntityTypesAsync(string workspace, CancellationToken cancellationToken)
    {
        var provider = sessions.Create(discoveryServiceUrl, workspace, identityProvider);
        try
        {
            var service = provider.RepositoryServices.Get<EntityType>();
            IEnumerable<string> names = service is IRepositoryServiceAsync<EntityType> asynchronous
                ? await asynchronous.GetNamesOfItemsAsync(cancellationToken)
                : await Task.Run(() => service.GetNamesOfItems(cancellationToken), cancellationToken);
            return names.Count();
        }
        finally
        {
            (provider as IDisposable)?.Dispose();
        }
    }
}
