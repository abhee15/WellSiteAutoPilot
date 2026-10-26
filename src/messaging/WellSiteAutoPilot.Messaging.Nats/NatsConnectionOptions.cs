using NATS.Client.Core;

namespace WellSiteAutoPilot.Messaging.Nats;

public static class NatsConnectionOptions
{
    public static NatsOpts Create(string url, string? token = null, NatsOpts? defaults = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        var options = defaults ?? NatsOpts.Default;
        return options with
        {
            Url = url,
            AuthOpts = token is null ? options.AuthOpts : options.AuthOpts with { Token = token }
        };
    }
}
