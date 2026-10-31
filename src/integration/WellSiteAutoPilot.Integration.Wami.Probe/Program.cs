using System.Diagnostics;
using WellSiteAutoPilot.Integration.Wami;
using WellSiteAutoPilot.Integration.Wami.NetTcp;

// Args: <router net.tcp uri> <model file> [wellheadPressure ...]
if (args.Length < 2)
{
    Console.Error.WriteLine("usage: probe <net.tcp://host:5656/Weatherford/IRouter> <model.wfl> [wellheadPressure ...]");
    return 2;
}

var client = new WamiNetTcpClient(new Uri(args[0]), TimeSpan.FromMinutes(2));
var content = await File.ReadAllBytesAsync(args[1]);
var model = new ModelArtifact(Path.GetFileName(args[1]), "probe", "wfl", content);
Console.WriteLine($"model {model.Ref.ModelId} {content.Length} bytes sha256 {model.Ref.Sha256[..12]}");

try
{
    var clock = Stopwatch.StartNew();
    var info = await client.GetServerInfoAsync();
    Console.WriteLine($"server version {info.ServerVersion}, licensed: {string.Join(",", info.LicensedFeatures)} ({clock.ElapsedMilliseconds} ms)");

    var sessions = int.TryParse(Environment.GetEnvironmentVariable("WAMI_PROBE_SESSIONS"), out var n) ? n : 0;
    if (sessions > 0)
    {
        // Holds N sessions open at once to find where the server starts refusing (MaxAllowedWorkers).
        var open = new List<IWamiModelSession>();
        try
        {
            for (var i = 1; i <= sessions; i++)
            {
                try
                {
                    open.Add(await client.OpenModelAsync(model));
                    Console.WriteLine($"session {i} opened");
                }
                catch (WamiException error)
                {
                    Console.WriteLine($"session {i} refused: {error.Code} ({error.Outcome}) {error.InnerException?.Message}");
                    break;
                }
            }
        }
        finally
        {
            foreach (var item in open) await item.DisposeAsync();
        }

        return 0;
    }

    await using var session = await client.OpenModelAsync(model);
    Console.WriteLine($"model opened ({clock.ElapsedMilliseconds} ms)");

    var runs = new List<Dictionary<string, WamiQuantity>> { new() };
    runs.AddRange(args.Skip(2).Select(a => new Dictionary<string, WamiQuantity> { [WamiNetTcpClient.WellheadPressure] = new(decimal.Parse(a, System.Globalization.CultureInfo.InvariantCulture), "psia") }));
    foreach (var inputs in runs)
    {
        clock.Restart();
        var result = await session.CalculateAsync(new WamiCalculationRequest(Guid.NewGuid().ToString("N"), WamiNetTcpClient.NodalAnalysis, inputs));
        var shown = inputs.Count == 0 ? "stored model" : "wellheadPressure=" + inputs[WamiNetTcpClient.WellheadPressure].Value;
        Console.WriteLine($"nodal-analysis [{shown}] in {clock.ElapsedMilliseconds} ms: " +
            string.Join(", ", result.Outputs.Select(o => $"{o.Key}={o.Value.Value}")) +
            (result.Warnings.Count > 0 ? $" status: {string.Join(";", result.Warnings)}" : ""));
    }

    return 0;
}
catch (WamiException error)
{
    Console.Error.WriteLine($"WAMI {error.Code} ({error.Outcome}): {error.Message}");
    if (error.InnerException is not null) Console.Error.WriteLine(error.InnerException.GetType().Name + ": " + error.InnerException.Message);
    return 1;
}
