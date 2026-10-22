using System.Text.Json;
using Weatherford.ForeSiteWellInputCheck;
using WellSiteAutoPilot.ModuleSdk;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: dotnet run --project OfflineRunner -- <fixture.json>");
    return 2;
}

try
{
    if (new FileInfo(args[0]).Length > 262144) throw new InvalidDataException("Fixture exceeds 256 KiB.");
    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true
    };
    using var document = JsonDocument.Parse(await File.ReadAllTextAsync(args[0]));
    var envelope = document.RootElement;
    if (envelope.GetProperty("contractVersion").GetInt32() != 1)
        throw new InvalidDataException("Only contractVersion 1 is supported.");
    var rawContext = envelope.GetProperty("context");
    var context = rawContext.Deserialize<LogicModuleExecutionContext>(options)
        ?? throw new InvalidDataException("Context is required.");
    var module = new EntryPoint();
    var result = await module.ExecuteAsync(context);
    // Keep original observations: SDK engineering results intentionally omit time/quality.
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        contractVersion = 1, mode = "OfflineShadow", context = rawContext, result
    }, options));
    return result.OutcomeCode == EntryPoint.Success ? 0 : 2;
}
catch (Exception error) when (error is IOException or UnauthorizedAccessException or
    JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
{
    Console.Error.WriteLine($"Invalid offline fixture: {error.GetType().Name}");
    return 2;
}
