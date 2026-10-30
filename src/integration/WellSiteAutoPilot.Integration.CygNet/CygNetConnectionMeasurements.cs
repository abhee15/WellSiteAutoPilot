using System.Globalization;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.CygNet;

/// <summary>
/// Reads one current value for a saved CygNet connection (POC). The domain comes from the connection;
/// the quantity-to-UDC map stays Gateway-wide.
/// </summary>
public sealed class CygNetConnectionMeasurements(CygNetProviderOptions options)
{
    public ValueTask<EngineeringValue> ReadAsync(ConnectionMeasurementRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Settings is null || !request.Settings.TryGetValue("domain", out var raw) ||
            !ushort.TryParse(raw?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var domain))
            throw new WellSiteAutoPilotException("CYGNET_CONNECTION_INVALID", FailureKind.Validation,
                "The CygNet connection has no valid domain.");
        return new CygNetCurrentDataProvider(options with { Domain = domain })
            .GetCurrentAsync(request.ExternalId, request.Quantity, cancellationToken);
    }
}
