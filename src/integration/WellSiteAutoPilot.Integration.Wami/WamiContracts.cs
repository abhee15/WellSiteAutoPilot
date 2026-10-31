using System.Security.Cryptography;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.Wami;

/// <summary>What a calculation function does to state beyond its own result.</summary>
public enum WamiFunctionEffect
{
    /// <summary>Reads the opened model only. Safe to repeat.</summary>
    None = 0,
    /// <summary>Changes the model held in the server session (never the stored model file). Repeat only on a fresh session.</summary>
    ChangesSessionModel = 1
}

public sealed record WamiQuantity(decimal Value, string Unit);

public sealed record WamiFunctionDescriptor(
    string Name,
    string Description,
    WamiFunctionEffect Effect,
    IReadOnlyList<string> Inputs,
    IReadOnlyList<string> Outputs,
    string? RequiredLicense = null);

public sealed record WamiServerInfo(string ServerVersion, IReadOnlyCollection<string> LicensedFeatures);

public sealed record WamiCalculationRequest(
    string RequestId,
    string Function,
    IReadOnlyDictionary<string, WamiQuantity> Inputs);

public sealed record WamiCalculationResult(
    string Function,
    IReadOnlyDictionary<string, WamiQuantity> Outputs,
    string ServerVersion,
    IReadOnlyList<string> Warnings);

/// <summary>Whether WAMI may have started work, so callers know if a repeat is safe.</summary>
public enum WamiCallOutcome
{
    /// <summary>Refused or failed before any work started (busy, unreachable, no licence).</summary>
    NotStarted = 0,
    /// <summary>WAMI answered with a definite failure.</summary>
    Failed = 1,
    /// <summary>The request may have run: timeout or cancel after dispatch. The result is unknown.</summary>
    Ambiguous = 2
}

public static class WamiFailureCodes
{
    public const string Unavailable = "WAMI_UNAVAILABLE";
    public const string Busy = "WAMI_BUSY";
    public const string Timeout = "WAMI_TIMEOUT";
    public const string LicenseMissing = "WAMI_LICENSE_MISSING";
    public const string ModelRejected = "WAMI_MODEL_REJECTED";
    public const string FunctionUnsupported = "WAMI_FUNCTION_UNSUPPORTED";
    public const string InputInvalid = "WAMI_INPUT_INVALID";
    public const string CalculationFailed = "WAMI_CALCULATION_FAILED";
}

public sealed class WamiException(string code, string message, WamiCallOutcome outcome, Exception? inner = null)
    : Exception(message, inner)
{
    public string Code { get; } = code;
    public WamiCallOutcome Outcome { get; } = outcome;
}

/// <summary>Model file bytes with the identity used to pin them. The hash is computed here, never trusted from a caller.</summary>
public sealed record ModelArtifactRef(string ModelId, string Revision, string Format, long SizeBytes, string Sha256);

public sealed class ModelArtifact
{
    public const int MaximumBytes = 8 * 1024 * 1024;

    public ModelArtifact(string modelId, string revision, string format, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length == 0 || content.Length > MaximumBytes)
            throw new ArgumentOutOfRangeException(nameof(content), "A model artifact must be 1 byte to 8 MiB.");
        Content = content;
        Ref = new ModelArtifactRef(modelId, revision, format, content.Length, Convert.ToHexStringLower(SHA256.HashData(content)));
    }

    public ModelArtifactRef Ref { get; }
    public byte[] Content { get; }
}

/// <summary>Supplies the model file that applies to one asset, as the provider that owns it (ForeSite) holds it.</summary>
public interface IModelArtifactSource
{
    ValueTask<ModelArtifact> GetCurrentModelAsync(string assetExternalId, CancellationToken cancellationToken = default);
}

/// <summary>A WAMI server. Calculations run against a model opened in one session, as WAMI itself works.</summary>
public interface IWamiClient
{
    IReadOnlyList<WamiFunctionDescriptor> Functions { get; }

    ValueTask<WamiServerInfo> GetServerInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens the model in a server session. Dispose the session to release the worker.</summary>
    ValueTask<IWamiModelSession> OpenModelAsync(ModelArtifact model, CancellationToken cancellationToken = default);
}

public interface IWamiModelSession : IAsyncDisposable
{
    ValueTask<WamiCalculationResult> CalculateAsync(WamiCalculationRequest request, CancellationToken cancellationToken = default);
}

public static class WamiAdapter
{
    public const string Key = "wami";
    public const string EndpointSetting = "endpoint";

    public static AdapterDescriptor Descriptor { get; } = new(
        Key,
        "WAMI",
        "Weatherford WAMI calculation service. Runs well-analysis calculations on a model file; it holds no wells or measurements of its own.",
        1,
        [new AdapterSettingField(
            EndpointSetting, "WAMI address",
            "The net.tcp address of the WAMI router, for example net.tcp://wami-server:5656/Weatherford/IRouter.",
            AdapterSettingKind.Text, true)],
        [new ProviderCapability(ProviderCapabilityKind.Calculation, "WellAnalysis")]);
}
