using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace WellSiteAutoPilot.Http;

public static class CorrelationContext
{
    public const string HeaderName = "X-Correlation-ID";

    internal const string ItemKey = "WellSiteAutoPilot.CorrelationId";

    public static string GetCorrelationId(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Items.TryGetValue(ItemKey, out var value) && value is string correlationId
            ? correlationId
            : context.TraceIdentifier;
    }

    public static string GetTraceId(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
    }
}
