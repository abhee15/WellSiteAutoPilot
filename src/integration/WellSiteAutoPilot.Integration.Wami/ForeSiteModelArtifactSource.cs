using System.Globalization;
using System.Net;
using System.Text.Json;
using WellSiteAutoPilot.Integration.ForeSite;

namespace WellSiteAutoPilot.Integration.Wami;

/// <summary>
/// Reads the current model file of a well through ForeSite's ModelFiles web service, as the Gateway's Windows account.
/// Read-only: only GET CurrentWellModel is used. The route and JSON shape come from ForeSite's source
/// (IModelFileService, ModelFileDTO) and are NOT yet confirmed against a running ForeSite.
/// </summary>
public sealed class ForeSiteModelArtifactSource(HttpClient client) : IModelArtifactSource
{
    public const string CurrentModelPath = "ModelFiles/CurrentWellModel/";
    public const string WamiFormat = "wami-model-file";

    private readonly ForeSiteSession _session = new(client);

    public async ValueTask<ModelArtifact> GetCurrentModelAsync(string assetExternalId, CancellationToken cancellationToken = default)
    {
        // ForeSite well ids are numeric; anything else must never reach a URL path.
        if (!long.TryParse(assetExternalId, NumberStyles.None, CultureInfo.InvariantCulture, out var wellId))
            throw new ArgumentException("A ForeSite well id is a whole number.", nameof(assetExternalId));

        using var response = await _session.GetAsync(
            CurrentModelPath + wellId.ToString(CultureInfo.InvariantCulture), cancellationToken, HttpCompletionOption.ResponseHeadersRead);
        if (response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.NotFound)
            throw new ModelSourceException("MODEL_NOT_FOUND", "ForeSite has no current model file for this well.");
        if (!response.IsSuccessStatusCode)
            throw new ModelSourceException("MODEL_SOURCE_FAILED", $"ForeSite answered {(int)response.StatusCode} for the model file.");
        // base64 inflates by a third; refuse before reading anything larger than the artifact limit allows.
        if (response.Content.Headers.ContentLength > ModelArtifact.MaximumBytes * 4L / 3 + 4096)
            throw new ModelSourceException("MODEL_TOO_LARGE", "The model file is larger than AutoPilot accepts.");

        JsonElement root;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new ModelSourceException("MODEL_SOURCE_INVALID", "ForeSite did not return valid JSON for the model file.");
        }

        if (root.ValueKind != JsonValueKind.Object || !TryGet(root, "Contents", out var contents) ||
            contents.ValueKind != JsonValueKind.String || !TryGet(root, "Id", out var id))
            throw new ModelSourceException("MODEL_SOURCE_INVALID", "ForeSite's model file answer lacks Id or Contents.");
        var applicable = TryGet(root, "ApplicableDate", out var date) && date.ValueKind == JsonValueKind.String ? date.GetString() : null;

        byte[] bytes;
        try { bytes = Convert.FromBase64String(contents.GetString()!); }
        catch (FormatException) { throw new ModelSourceException("MODEL_SOURCE_INVALID", "The model file contents are not base64."); }
        if (bytes.Length is 0 or > ModelArtifact.MaximumBytes)
            throw new ModelSourceException("MODEL_TOO_LARGE", "The model file is empty or larger than AutoPilot accepts.");

        var modelId = id.ToString();
        return new ModelArtifact(modelId, applicable is null ? modelId : $"{modelId}@{applicable}", WamiFormat, bytes);
    }

    private static bool TryGet(JsonElement root, string name, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }
}

public sealed class ModelSourceException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
