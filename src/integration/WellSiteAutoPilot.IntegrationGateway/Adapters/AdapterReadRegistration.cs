using System.Text.Json;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.IntegrationGateway.Adapters;

/// <summary>Local Gateway registration pairing a catalog operation with its typed handler.</summary>
public sealed class AdapterReadRegistration
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };
    private readonly Func<AdapterReadInvocation<JsonElement>, IServiceProvider, CancellationToken, Task<byte[]>> _invoke;

    private AdapterReadRegistration(AdapterOperationDescriptor operation,
        Func<AdapterReadInvocation<JsonElement>, IServiceProvider, CancellationToken, Task<byte[]>> invoke)
    {
        Operation = operation;
        _invoke = invoke;
    }

    public AdapterOperationDescriptor Operation { get; }

    public static AdapterReadRegistration Create<TRequest, TResponse>(AdapterOperationDescriptor operation,
        Func<TRequest, AdapterReadInvocation<JsonElement>, IServiceProvider, CancellationToken, Task<TResponse>> read)
        where TRequest : class
    {
        return new(operation, async (invocation, services, cancellationToken) =>
        {
            TRequest? request;
            try { request = invocation.Input.Deserialize<TRequest>(JsonOptions); }
            catch (JsonException) { throw AdapterReadEndpoints.InvalidRequest(); }

            if (request is null) throw AdapterReadEndpoints.InvalidRequest();
            var result = await read(request, invocation, services, cancellationToken);
            if (result is null) throw AdapterReadEndpoints.InvalidResponse();

            var bytes = JsonSerializer.SerializeToUtf8Bytes(result, JsonOptions);
            if (bytes.Length > operation.Limits.MaximumReplyBytes)
                throw AdapterReadEndpoints.InvalidResponse();
            return bytes;
        });
    }

    internal Task<byte[]> InvokeAsync(AdapterReadInvocation<JsonElement> invocation, IServiceProvider services, CancellationToken cancellationToken) =>
        _invoke(invocation, services, cancellationToken);
}

public sealed record AdapterRegistration(AdapterDescriptor Descriptor, IReadOnlyList<AdapterReadRegistration> Reads);
