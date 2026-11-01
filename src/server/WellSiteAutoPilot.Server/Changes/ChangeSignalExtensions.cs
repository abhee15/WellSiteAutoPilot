using Microsoft.Extensions.Options;
using WellSiteAutoPilot.Application.Changes;

namespace WellSiteAutoPilot.Server.Changes;

public static class ChangeSignalExtensions
{
    public static IServiceCollection AddChangeSignal(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<ChangeSignalOptions>(configuration.GetSection(ChangeSignalOptions.SectionName));
        services.AddSignalR();
        services.AddSingleton<ChangeConnectionRegistry>();
        services.AddScoped<IChangeAreaAccess, ChangeAreaAccess>();
        services.AddSingleton<IChangeSender, HubChangeSender>();
        services.AddSingleton<ChangeSignalService>();
        services.AddSingleton<IChangeNotifier>(provider => provider.GetRequiredService<ChangeSignalService>());
        services.AddHostedService(provider => provider.GetRequiredService<ChangeSignalService>());
        services.AddSingleton<IChangeEventSender, HubChangeEventSender>();
        services.AddSingleton<ChangeEventService>();
        services.AddSingleton<IChangeEventPublisher>(provider => provider.GetRequiredService<ChangeEventService>());
        services.AddHostedService(provider => provider.GetRequiredService<ChangeEventService>());
        services.AddHostedService<ChangeConnectionMonitor>();
        return services;
    }

    /// <summary>Maps the hub. It requires an authenticated user via the host's default scheme.</summary>
    public static IEndpointConventionBuilder MapChangeSignal(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var transports = endpoints.ServiceProvider.GetRequiredService<IOptions<ChangeSignalOptions>>()
            .Value.EffectiveTransports;
        return endpoints.MapHub<ChangesHub>(ChangeSignalProtocol.Route, connection => connection.Transports = transports);
    }

    /// <summary>
    /// Raises the area after the endpoint handler has returned, that is after its service call
    /// committed. A handler that throws raises nothing. Never changes the response.
    /// </summary>
    public static TBuilder NotifiesChange<TBuilder>(this TBuilder builder, ChangeArea area)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (invocation, next) =>
        {
            var result = await next(invocation);
            invocation.HttpContext.RequestServices.GetRequiredService<IChangeNotifier>().Notify(area);
            return result;
        });
}
