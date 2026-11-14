using WellSiteAutoPilot.Application.Connections;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Application.Logic;

public interface IGuidedHistoryGateway
{
    Task<HistoricalReadResult> ReadAsync(ConnectionRecord connection, AdapterOperationDescriptor operation, HistoricalReadRequest read,
        CancellationToken cancellationToken = default);
}
