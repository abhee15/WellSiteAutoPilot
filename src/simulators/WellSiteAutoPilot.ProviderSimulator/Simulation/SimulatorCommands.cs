using System.Collections.Concurrent;

namespace WellSiteAutoPilot.ProviderSimulator.Simulation;

/// <summary>
/// How the simulated provider misbehaves when a command arrives. These mirror the ambiguous outcomes a real provider
/// produces, so the whole command path can be proven in CI without one.
/// </summary>
public enum SimulatorCommandFault
{
    None = 0,

    /// <summary>The provider refuses the command and does nothing (a definite "no").</summary>
    Reject = 1,

    /// <summary>The call fails before the provider acts: nothing changes, but the caller cannot know that.</summary>
    FailBeforeApply = 2,

    /// <summary>The provider acts, then the call fails: the value changed but the caller sees an error.</summary>
    FailAfterApply = 3,

    /// <summary>The provider accepts at once and the value only changes after the configured delay.</summary>
    SlowApply = 4,

    /// <summary>The provider acts, but on a different value than was sent.</summary>
    WrongValue = 5,

    /// <summary>The provider acts, but the value cannot be read back afterwards.</summary>
    ReadbackUnavailable = 6
}

/// <summary>A command the simulated provider understands. Its limits are the provider's own, not a site's.</summary>
public sealed record SimulatorCommandDefinition(
    string Name, string ReadbackQuantity, string? Unit, string ReadbackUnit, decimal Minimum, decimal Maximum, bool WholeNumber);

public enum SimulatorCommandOutcome
{
    Accepted,
    Rejected,
    Failed,
    UnknownCommand
}

public sealed record SimulatorCommandResult(SimulatorCommandOutcome Outcome, string? Code, string? Reference);

public sealed record SimulatorCommandLog(int Received, int Applied, int Duplicates, IReadOnlyDictionary<string, int> ByRequest);

/// <summary>
/// The simulated provider's command side. A command changes the current value that the read side serves, so a read-back
/// through the ordinary read path shows whether it took effect. The provider has no idempotency of its own: a repeated
/// request is applied again and counted, so a test can prove the platform sent each command exactly once.
/// </summary>
public sealed class SimulatorCommandBoard(SimulatorState state)
{
    public static readonly IReadOnlyList<SimulatorCommandDefinition> Definitions =
    [
        new("SET_SPEED", "CurrentSPM", "spm", "spm", 0m, 15m, false),
        new("PUMP_RUN", "PumpRunning", null, "state", 0m, 1m, true)
    ];

    private readonly ConcurrentDictionary<string, int> _byRequest = new();
    private int _received;
    private int _applied;
    private int _duplicates;

    public SimulatorCommandFault Fault { get; private set; } = SimulatorCommandFault.None;

    public int DelaySeconds { get; private set; }

    public void SetFault(SimulatorCommandFault fault, int delaySeconds)
    {
        Fault = fault;
        DelaySeconds = Math.Clamp(delaySeconds, 0, 60);
        state.SetReadbackBlocked(fault == SimulatorCommandFault.ReadbackUnavailable
            ? Definitions.Select(item => item.ReadbackQuantity)
            : []);
    }

    public void Reset()
    {
        Fault = SimulatorCommandFault.None;
        DelaySeconds = 0;
        _byRequest.Clear();
        Interlocked.Exchange(ref _received, 0);
        Interlocked.Exchange(ref _applied, 0);
        Interlocked.Exchange(ref _duplicates, 0);
    }

    public SimulatorCommandLog Log() =>
        new(_received, _applied, _duplicates, new Dictionary<string, int>(_byRequest));

    public SimulatorCommandResult Execute(string assetId, string command, string requestId, decimal value, string? unit)
    {
        var definition = Definitions.FirstOrDefault(item => string.Equals(item.Name, command, StringComparison.Ordinal));
        if (definition is null) return new(SimulatorCommandOutcome.UnknownCommand, "UNKNOWN_COMMAND", null);

        Interlocked.Increment(ref _received);
        if (_byRequest.AddOrUpdate(requestId, 1, (_, count) => count + 1) > 1) Interlocked.Increment(ref _duplicates);

        var fault = Fault;
        if (fault == SimulatorCommandFault.Reject) return new(SimulatorCommandOutcome.Rejected, "REJECTED_BY_PROVIDER", null);
        if (fault == SimulatorCommandFault.FailBeforeApply) return new(SimulatorCommandOutcome.Failed, "PROVIDER_UNAVAILABLE", null);

        // The provider's own limits apply however the command got here.
        if (unit is not null && !string.Equals(unit, definition.Unit, StringComparison.Ordinal))
            return new(SimulatorCommandOutcome.Rejected, "UNIT_MISMATCH", null);
        if (value < definition.Minimum || value > definition.Maximum ||
            (definition.WholeNumber && decimal.Truncate(value) != value))
            return new(SimulatorCommandOutcome.Rejected, "VALUE_OUT_OF_RANGE", null);

        var applied = fault == SimulatorCommandFault.WrongValue ? Math.Min(definition.Maximum, value + 1m) : value;
        if (fault == SimulatorCommandFault.WrongValue && applied == value) applied = Math.Max(definition.Minimum, value - 1m);
        var due = fault == SimulatorCommandFault.SlowApply
            ? DateTimeOffset.UtcNow.AddSeconds(DelaySeconds)
            : DateTimeOffset.UtcNow;
        state.ScheduleCurrent(assetId, definition.ReadbackQuantity, applied, definition.ReadbackUnit, due);
        Interlocked.Increment(ref _applied);

        return fault == SimulatorCommandFault.FailAfterApply
            ? new(SimulatorCommandOutcome.Failed, "PROVIDER_TIMEOUT", null)
            : new(SimulatorCommandOutcome.Accepted, null, $"sim-{_applied}");
    }
}

public sealed record SimulatorCommandRequest(string RequestId, decimal Value, string? Unit = null);

public sealed record SetCommandFaultRequest(string Fault, int DelaySeconds = 0);
