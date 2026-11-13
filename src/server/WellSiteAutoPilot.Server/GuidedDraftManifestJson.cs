using System.Text.Json;
using System.Text.Json.Serialization;
using WellSiteAutoPilot.Domain.Logic;

namespace WellSiteAutoPilot.Server;

internal static class GuidedDraftManifestJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(LogicModuleManifest manifest) => JsonSerializer.Serialize(manifest, Options);
}
