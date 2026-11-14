using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Http.ServiceCallers;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.IntegrationGateway.Adapters;

public static class AdapterReadEndpoints
{
    public static void MapRegisteredAdapterReads(this RouteGroupBuilder routes) =>
        routes.MapPost("/adapters/{adapterKey}/reads/{operationKey}", ReadAsync)
            .WithName("ReadRegisteredAdapterOperation")
            .RequireAuthorization(ServiceCaller.ServerPolicy)
            .WithMetadata(new RequestSizeLimitAttribute(MeasurementPayloadLimits.MaximumReplyBytes));

    private static async Task<IResult> ReadAsync(string adapterKey, string operationKey, AdapterReadInvocation<JsonElement> request,
        AdapterCatalog catalog, HttpContext context, CancellationToken cancellationToken)
    {
        var registration = catalog.FindRead(adapterKey, operationKey);
        if (registration is null)
            return Results.NotFound();

        var operation = registration.Operation;
        if (request.ContractVersion != operation.ContractVersion)
            return Results.Conflict();

        if (request.ConnectionId == Guid.Empty || request.ConnectionRevision < 1 || request.Settings is null ||
            request.ResourceKind != operation.ResourceKind ||
            string.IsNullOrWhiteSpace(request.ExternalId) || request.ExternalId.Length > 128 ||
            request.ExternalId.Any(char.IsControl) || request.Input.ValueKind != JsonValueKind.Object ||
            AdapterSettingsValidator.Validate(catalog.Find(adapterKey)!, request.Settings).Count != 0)
            throw InvalidRequest();

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(operation.Limits.TimeoutSeconds!.Value));
        try
        {
            var bytes = await registration.InvokeAsync(request, context.RequestServices, budget.Token);
            budget.Token.ThrowIfCancellationRequested();
            return Results.Bytes(bytes, "application/json");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && budget.IsCancellationRequested)
        {
            throw new WellSiteAutoPilotException("ADAPTER_READ_TIMEOUT", FailureKind.DependencyTimeout,
                "The adapter did not complete the read in time.");
        }
    }

    internal static WellSiteAutoPilotException InvalidRequest() =>
        new("ADAPTER_READ_REQUEST_INVALID", FailureKind.Validation, "The adapter read request does not match the registered operation.");

    internal static WellSiteAutoPilotException InvalidResponse() =>
        new("ADAPTER_READ_RESPONSE_INVALID", FailureKind.DependencyUnavailable, "The adapter returned an invalid read response.");
}
