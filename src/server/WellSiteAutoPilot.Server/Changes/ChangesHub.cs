using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using WellSiteAutoPilot.Application.Changes;

namespace WellSiteAutoPilot.Server.Changes;

/// <summary>
/// Sends only an area name, and only to connections whose user holds that area's read
/// permission. The hub exposes no client-callable methods and carries no data: the browser
/// refetches through the authorized REST endpoints, so scope filtering stays in one place.
/// Authentication is the host's single scheme, exactly as for the REST API.
/// </summary>
[Authorize]
public sealed class ChangesHub(IChangeAreaAccess access, ChangeConnectionRegistry registry) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        if (user is null)
        {
            Context.Abort();
            return;
        }

        var areas = await access.GetReadableAreasAsync(user, Context.ConnectionAborted);
        if (areas.Count == 0)
        {
            // Nothing to hear about; the client falls back to polling.
            Context.Abort();
            return;
        }

        registry.Add(new ChangeConnection(Context.ConnectionId, user, areas, Context.Abort));
        foreach (var area in areas)
            await Groups.AddToGroupAsync(Context.ConnectionId, ChangeSignalProtocol.GroupName(area),
                Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        registry.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
