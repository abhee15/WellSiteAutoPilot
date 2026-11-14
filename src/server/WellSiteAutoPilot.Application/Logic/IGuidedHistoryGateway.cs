using WellSiteAutoPilot.Application.Connections;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Application.Logic;

public interface IGuidedHistoryGateway
{
    Task<HistoricalReadResult> ReadAsync(ConnectionRecord connection, HistoricalReadRequest read,
        CancellationToken cancellationToken = default);
}
