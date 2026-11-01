using Microsoft.AspNetCore.Http.Connections;

namespace WellSiteAutoPilot.Server.Changes;

public sealed class ChangeSignalOptions
{
    public const string SectionName = "ChangeSignal";
    public const int DefaultRecheckSeconds = 60;

    /// <summary>
    /// At most this long after a user loses an area's read permission, the
    /// connection is closed (ADR-008 decision 4).
    /// </summary>
    public int RecheckSeconds { get; set; } = DefaultRecheckSeconds;

    /// <summary>
    /// Sends typed entity events (execution and recommendation changes) in addition to the area
    /// signals. Off by default; area signals are unchanged either way.
    /// </summary>
    public bool TypedEvents { get; set; }

    /// <summary>Bursts of changes inside this window are sent as one message per area.</summary>
    public int CoalesceMilliseconds { get; set; } = 250;

    /// <summary>
    /// Allowed hub transports, for example "WebSockets, LongPolling". Browsers cannot attach
    /// an Authorization header to a WebSocket, so long polling stays available as a fallback
    /// until Windows Negotiate behaviour is verified in a real browser (issue #92).
    /// </summary>
    public HttpTransportType Transports { get; set; } =
        HttpTransportType.WebSockets | HttpTransportType.LongPolling;

    public TimeSpan RecheckInterval => TimeSpan.FromSeconds(Math.Clamp(RecheckSeconds, 1, 3600));

    public TimeSpan CoalesceWindow => TimeSpan.FromMilliseconds(Math.Clamp(CoalesceMilliseconds, 0, 5000));

    public HttpTransportType EffectiveTransports =>
        Transports == HttpTransportType.None
            ? HttpTransportType.WebSockets | HttpTransportType.LongPolling
            : Transports;
}
