using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace WellSiteAutoPilot.Telemetry;

public static class TelemetryServiceCollectionExtensions
{
    /// <summary>NATS client activity source, so publish and consume spans join the same trace.</summary>
    private const string NatsSourceName = "NATS.Net";

    /// <summary>
    /// Adds tracing and metrics for one process. Health probes are excluded. Export happens only
    /// when <c>Telemetry:OtlpEndpoint</c> or the standard <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set,
    /// so an offline runtime has no Internet dependency.
    /// </summary>
    public static IServiceCollection AddWellSiteTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var options = configuration.GetSection(TelemetryOptions.SectionName).Get<TelemetryOptions>()
            ?? new TelemetryOptions();
        if (!options.Enabled)
        {
            return services;
        }

        var export =
            !string.IsNullOrWhiteSpace(options.OtlpEndpoint) ||
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"));
        var version = typeof(WellSiteTelemetry).Assembly.GetName().Version?.ToString() ?? "0.0.0";

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName, serviceVersion: version))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(WellSiteTelemetry.SourceName)
                    .AddSource(NatsSourceName)
                    .AddAspNetCoreInstrumentation(instrumentation =>
                        instrumentation.Filter = context =>
                            !context.Request.Path.StartsWithSegments("/health"))
                    .AddHttpClientInstrumentation();
                if (export)
                {
                    tracing.AddOtlpExporter(exporter => Configure(exporter, options.OtlpEndpoint));
                }
            })
            .WithMetrics(metrics =>
            {
                metrics.AddMeter("WellSiteAutoPilot.*").AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();
                if (export)
                {
                    metrics.AddOtlpExporter(exporter => Configure(exporter, options.OtlpEndpoint));
                }
            });

        return services;
    }

    private static void Configure(OpenTelemetry.Exporter.OtlpExporterOptions exporter, string? endpoint)
    {
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            exporter.Endpoint = new Uri(endpoint, UriKind.Absolute);
        }
    }
}
