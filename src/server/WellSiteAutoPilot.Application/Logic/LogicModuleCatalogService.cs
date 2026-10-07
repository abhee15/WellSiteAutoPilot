using System.Text.Json;
using System.Text.Json.Serialization;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Logic;

public sealed class LogicModuleCatalogService(
    ILogicModuleCatalogRepository repository,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public async Task<InstalledLogicModule> RegisterAsync(
        RegisterLogicModuleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var manifest = DeserializeManifest(command.ManifestJson);
        LogicModuleManifestValidator.Validate(manifest);

        var checksum = NormalizeSha256(command.PackageSha256);
        ValidateTrustStatus(command.TrustStatus);

        var existing = await repository.GetAsync(
            manifest.ModuleId,
            manifest.Version,
            cancellationToken);

        if (existing is not null)
        {
            if (string.Equals(
                    existing.PackageSha256,
                    checksum,
                    StringComparison.OrdinalIgnoreCase))
            {
                return existing;
            }

            throw new WellSiteAutoPilotException(
                "MODULE_VERSION_IMMUTABLE",
                FailureKind.Conflict,
                $"Logic Module '{manifest.ModuleId}' version '{manifest.Version}' is already installed with different package content.");
        }

        var module = new InstalledLogicModule(
            Guid.NewGuid(),
            manifest.ModuleId,
            manifest.Version,
            manifest.DisplayName,
            manifest.Publisher,
            manifest.Runtime,
            manifest.ExecutionProfile,
            NormalizeJson(command.ManifestJson),
            checksum,
            command.TrustStatus,
            command.TrustStatus is
                LogicModuleTrustStatus.TrustedPublisher or
                LogicModuleTrustStatus.CustomerApproved,
            timeProvider.GetUtcNow());

        await repository.AddAsync(module, cancellationToken);
        return module;
    }

    public async Task<InstalledLogicModule> GetRequiredAsync(
        string moduleId,
        string version,
        CancellationToken cancellationToken = default)
    {
        var normalizedModuleId = RequireText(moduleId, "Module ID is required.");
        var normalizedVersion = RequireText(version, "Module version is required.");

        return await repository.GetAsync(
                   normalizedModuleId,
                   normalizedVersion,
                   cancellationToken) ??
               throw new WellSiteAutoPilotException(
                   "MODULE_NOT_INSTALLED",
                   FailureKind.NotFound,
                   $"Logic Module '{normalizedModuleId}' version '{normalizedVersion}' is not installed.");
    }

    public Task<IReadOnlyCollection<InstalledLogicModule>> ListAsync(
        string? moduleId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 500)
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed,
                FailureKind.Validation,
                "Logic Module catalog limit must be between 1 and 500.");
        }

        return repository.ListAsync(
            string.IsNullOrWhiteSpace(moduleId) ? null : moduleId.Trim(),
            limit,
            cancellationToken);
    }

    private static LogicModuleManifest DeserializeManifest(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed,
                FailureKind.Validation,
                "Logic Module manifest JSON is required.");
        }

        try
        {
            return JsonSerializer.Deserialize<LogicModuleManifest>(
                       json,
                       SerializerOptions) ??
                   throw new WellSiteAutoPilotException(
                       FailureCodes.ValidationFailed,
                       FailureKind.Validation,
                       "Logic Module manifest JSON is empty.");
        }
        catch (JsonException exception)
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed,
                FailureKind.Validation,
                "Logic Module manifest JSON is invalid.",
                innerException: exception);
        }
    }

    private static string NormalizeSha256(string checksum)
    {
        var value = RequireText(checksum, "Package SHA-256 is required.").ToLowerInvariant();

        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new WellSiteAutoPilotException(
                "MODULE_PACKAGE_CHECKSUM_INVALID",
                FailureKind.Validation,
                "Package SHA-256 must be a 64-character hexadecimal value.");
        }

        return value;
    }

    private static void ValidateTrustStatus(LogicModuleTrustStatus trustStatus)
    {
        if (!Enum.IsDefined(trustStatus))
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed,
                FailureKind.Validation,
                "Logic Module trust status is not recognized.");
        }
    }

    private static string NormalizeJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement, SerializerOptions);
    }

    private static string RequireText(string value, string detail)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed,
                FailureKind.Validation,
                detail);
        }

        return value.Trim();
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
