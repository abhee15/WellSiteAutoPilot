using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Http;

public sealed partial class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        var failure = FailureClassifier.Classify(exception);
        var correlationId = CorrelationContext.GetCorrelationId(httpContext);
        var traceId = CorrelationContext.GetTraceId(httpContext);

        if (failure.Kind == FailureKind.Unexpected)
        {
            LogUnexpectedFailure(logger, exception, failure.Code, correlationId, traceId);
        }
        else if (failure.Retryable)
        {
            LogRetryableFailure(logger, exception, failure.Code, correlationId, traceId);
        }
        else
        {
            LogHandledFailure(logger, failure.Code, correlationId, traceId);
        }

        if (httpContext.Response.HasStarted)
        {
            return false;
        }

        var statusCode = ToStatusCode(failure.Kind);
        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/problem+json";

        var problem = new WellSiteProblemDetails
        {
            Type = $"urn:wellsite-autopilot:error:{failure.Code.ToLowerInvariant()}",
            Title = failure.Title,
            Status = statusCode,
            Detail = failure.Detail,
            Instance = httpContext.Request.Path,
            Code = failure.Code,
            CorrelationId = correlationId,
            TraceId = traceId,
            Retryable = failure.Retryable
        };

        await httpContext.Response.WriteAsJsonAsync(
            problem,
            cancellationToken: cancellationToken);

        return true;
    }

    private static int ToStatusCode(FailureKind kind) => kind switch
    {
        FailureKind.Validation => StatusCodes.Status400BadRequest,
        FailureKind.NotFound => StatusCodes.Status404NotFound,
        FailureKind.Conflict => StatusCodes.Status409Conflict,
        FailureKind.Authorization => StatusCodes.Status403Forbidden,
        FailureKind.DependencyUnavailable => StatusCodes.Status503ServiceUnavailable,
        FailureKind.DependencyTimeout => StatusCodes.Status504GatewayTimeout,
        FailureKind.RequestCancelled => 499,
        FailureKind.Unexpected => StatusCodes.Status500InternalServerError,
        _ => StatusCodes.Status500InternalServerError
    };

    [LoggerMessage(
        EventId = 9000,
        Level = LogLevel.Error,
        Message = "Unhandled failure {FailureCode}. CorrelationId={CorrelationId} TraceId={TraceId}")]
    private static partial void LogUnexpectedFailure(
        ILogger logger,
        Exception exception,
        string failureCode,
        string correlationId,
        string traceId);

    [LoggerMessage(
        EventId = 9001,
        Level = LogLevel.Warning,
        Message = "Retryable failure {FailureCode}. CorrelationId={CorrelationId} TraceId={TraceId}")]
    private static partial void LogRetryableFailure(
        ILogger logger,
        Exception exception,
        string failureCode,
        string correlationId,
        string traceId);

    [LoggerMessage(
        EventId = 9002,
        Level = LogLevel.Information,
        Message = "Handled failure {FailureCode}. CorrelationId={CorrelationId} TraceId={TraceId}")]
    private static partial void LogHandledFailure(
        ILogger logger,
        string failureCode,
        string correlationId,
        string traceId);
}
