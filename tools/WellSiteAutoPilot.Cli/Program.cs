using System.Text.Json;
using System.Text.Json.Serialization;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;

return await Cli.RunAsync(args);

internal static class Cli
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 3 &&
            string.Equals(args[0], "module", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(args[1], "validate", StringComparison.OrdinalIgnoreCase))
        {
            return await ValidateManifestAsync(args[2]);
        }

        WriteUsage();
        return args.Length == 0 ? 0 : 2;
    }

    private static async Task<int> ValidateManifestAsync(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);

            if (!File.Exists(fullPath))
            {
                Console.Error.WriteLine($"Manifest file was not found: {fullPath}");
                return 2;
            }

            await using var stream = File.OpenRead(fullPath);
            var manifest = await JsonSerializer.DeserializeAsync<LogicModuleManifest>(
                stream,
                SerializerOptions);

            if (manifest is null)
            {
                Console.Error.WriteLine("Manifest file did not contain a Logic Module manifest.");
                return 2;
            }

            LogicModuleManifestValidator.Validate(manifest);

            Console.WriteLine(
                $"Valid Logic Module manifest: {manifest.ModuleId} {manifest.Version}");
            return 0;
        }
        catch (JsonException exception)
        {
            Console.Error.WriteLine($"Manifest JSON is invalid: {exception.Message}");
            return 2;
        }
        catch (WellSiteAutoPilotException exception)
        {
            Console.Error.WriteLine($"{exception.Code}: {exception.SafeDetail}");
            return 2;
        }
    }

    private static void WriteUsage()
    {
        Console.WriteLine("WellSite AutoPilot CLI");
        Console.WriteLine("  module validate <manifest.json>");
    }
}
