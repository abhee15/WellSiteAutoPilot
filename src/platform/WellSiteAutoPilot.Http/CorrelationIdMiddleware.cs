using Microsoft.AspNetCore.Http;

namespace WellSiteAutoPilot.Http;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    private const int MaximumLength = 128;

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var incoming = context.Request.Headers[CorrelationContext.HeaderName].FirstOrDefault();
        var correlationId = IsValid(incoming)
            ? incoming!
            : Guid.NewGuid().ToString("N");

        context.Items[CorrelationContext.ItemKey] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationContext.HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        await next(context);
    }

    private static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumLength)
        {
            return false;
        }

        return value.All(character =>
            char.IsLetterOrDigit(character) ||
            character is '-' or '_' or '.' or ':' or '/');
    }
}
