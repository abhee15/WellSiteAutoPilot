using Microsoft.Extensions.DependencyInjection;
using NATS.Extensions.Microsoft.DependencyInjection;

namespace WellSiteAutoPilot.Messaging.Nats;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWellSiteMessaging(
        this IServiceCollection services,
        string natsUrl)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(natsUrl);

        services.AddNatsClient(nats =>
        {
            nats.ConfigureOptions(options =>
                options.Configure(configuration =>
                    configuration.Opts = configuration.Opts with
                    {
                        Url = natsUrl
                    }));
        });

        return services;
    }
}
