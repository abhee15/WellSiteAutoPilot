using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Weatherford.WellSiteAutoPilot.Nats";
});
builder.Services.AddHostedService<NatsServerHost>();

await builder.Build().RunAsync();

internal sealed partial class NatsServerHost(
    ILogger<NatsServerHost> logger) : BackgroundService
{
    private Process? _process;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var executable = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "NATS",
                "nats-server.exe"));

        if (!File.Exists(executable))
        {
            throw new FileNotFoundException(
                "Bundled NATS server executable was not found.",
                executable);
        }

        var programDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Weatherford",
            "WellSite AutoPilot",
            "NATS");

        var storeDirectory = Path.Combine(programDataRoot, "JetStream");
        var pidFile = Path.Combine(programDataRoot, "nats-server.pid");

        Directory.CreateDirectory(storeDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-js");
        startInfo.ArgumentList.Add("-a");
        startInfo.ArgumentList.Add("127.0.0.1");
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add("4222");
        startInfo.ArgumentList.Add("-sd");
        startInfo.ArgumentList.Add(storeDirectory);
        startInfo.ArgumentList.Add("-P");
        startInfo.ArgumentList.Add(pidFile);

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        _process = process;

        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                LogNatsOutput(logger, eventArgs.Data);
            }
        };

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                LogNatsError(logger, eventArgs.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start the bundled NATS server.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        LogNatsStarted(logger, process.Id);

        try
        {
            await process.WaitForExitAsync(stoppingToken);

            if (!stoppingToken.IsCancellationRequested && process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Bundled NATS server exited unexpectedly with code {process.ExitCode}.");
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            await StopNatsAsync(executable, process);
        }
        finally
        {
            _process = null;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        var process = _process;
        if (process is { HasExited: false })
        {
            var executable = Path.GetFullPath(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "..",
                    "NATS",
                    "nats-server.exe"));

            await StopNatsAsync(executable, process);
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task StopNatsAsync(string executable, Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        try
        {
            using var signal = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            signal.StartInfo.ArgumentList.Add("--signal");
            signal.StartInfo.ArgumentList.Add($"stop={process.Id}");

            signal.Start();
            await signal.WaitForExitAsync();

            if (signal.ExitCode == 0)
            {
                var exited = await WaitForExitAsync(process, TimeSpan.FromSeconds(10));
                if (exited)
                {
                    LogNatsStopped(logger);
                    return;
                }
            }
        }
        catch (InvalidOperationException exception)
        {
            LogNatsStopFallback(logger, exception);
        }

        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            LogNatsKilled(logger);
        }
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Information,
        Message = "Bundled NATS server started with process ID {ProcessId}.")]
    private static partial void LogNatsStarted(ILogger logger, int processId);

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Debug,
        Message = "NATS: {Message}")]
    private static partial void LogNatsOutput(ILogger logger, string message);

    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Warning,
        Message = "NATS stderr: {Message}")]
    private static partial void LogNatsError(ILogger logger, string message);

    [LoggerMessage(
        EventId = 3003,
        Level = LogLevel.Information,
        Message = "Bundled NATS server stopped gracefully.")]
    private static partial void LogNatsStopped(ILogger logger);

    [LoggerMessage(
        EventId = 3004,
        Level = LogLevel.Warning,
        Message = "Graceful NATS shutdown failed; falling back to process termination.")]
    private static partial void LogNatsStopFallback(
        ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 3005,
        Level = LogLevel.Warning,
        Message = "Bundled NATS server process was force-terminated.")]
    private static partial void LogNatsKilled(ILogger logger);
}
