using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WellSiteAutoPilot.Application.Changes;
using WellSiteAutoPilot.Server.Changes;

/// <summary>
/// Typed change events ride next to the area signal: off by default, numbered per area stream,
/// sent in order, and never able to fail or slow the producer that published them.
/// </summary>
internal static class ChangeEventChecks
{
    public static async Task RunAsync()
    {
        await CheckDisabledAsync();
        await CheckSequencingAsync();
        await CheckFailureAndOverloadAsync();
        await CheckHostedLoopAsync();
        Console.WriteLine("Typed change events are opt-in, numbered per area, ordered and never fail producers.");
    }

    private static ChangeEventService Create(Sender sender, bool enabled) =>
        new(sender, Options.Create(new ChangeSignalOptions { TypedEvents = enabled }), TimeProvider.System,
            NullLogger<ChangeEventService>.Instance);

    private static ChangeEventDraft Draft(ChangeArea area, string kind, Guid? id = null) =>
        new(area, area == ChangeArea.Executions ? ChangeEventNames.Execution : ChangeEventNames.Recommendation,
            id ?? Guid.NewGuid(), kind);

    private static async Task CheckDisabledAsync()
    {
        Require(!new ChangeSignalOptions().TypedEvents, "Typed events must be off by default.");
        Require(ChangeSignalProtocol.EventMethod == "event" && ChangeSignalProtocol.ClientMethod == "changed",
            "The typed message must sit next to, not replace, the area signal.");
        var sender = new Sender();
        var service = Create(sender, false);
        service.Publish(Draft(ChangeArea.Executions, ChangeEventNames.Started));
        await service.FlushAsync(CancellationToken.None);
        Require(sender.Sent.Count == 0, "An event was sent while the flag was off.");
        NoOpChangeEventPublisher.Instance.Publish(Draft(ChangeArea.Executions, ChangeEventNames.Started));
    }

    private static async Task CheckSequencingAsync()
    {
        var sender = new Sender();
        var service = Create(sender, true);
        var run = Guid.NewGuid();
        service.Publish(Draft(ChangeArea.Executions, ChangeEventNames.Requested, run));
        service.Publish(Draft(ChangeArea.Recommendations, ChangeEventNames.Created));
        service.Publish(Draft(ChangeArea.Executions, ChangeEventNames.Failed, run) with
        {
            Actor = "CI\\operator", CorrelationId = "corr-1",
            Payload = new Dictionary<string, string?> { ["status"] = "Failed" }
        });
        service.Publish(Draft((ChangeArea)42, "ignored"));
        await service.FlushAsync(CancellationToken.None);

        Require(sender.Sent.Count == 3, "An unknown area was published or an event was lost.");
        var executions = sender.Sent.Where(item => item.Area == "executions").ToArray();
        var recommendations = sender.Sent.Single(item => item.Area == "recommendations");
        Require(executions.Select(item => item.Sequence).SequenceEqual([1L, 2L]),
            "Execution events were not numbered 1, 2 in publish order.");
        Require(recommendations.Sequence == 1, "Another area's events must not consume this stream's numbers.");
        Require(sender.Sent.All(item => item.Epoch == service.Epoch && item.Version == 1),
            "Every event must carry the current epoch and the wire version.");
        var failed = executions[1];
        Require(failed.Kind == "failed" && failed.Entity == "execution" && failed.Id == run &&
            failed.Actor == "CI\\operator" && failed.CorrelationId == "corr-1" && failed.Payload["status"] == "Failed",
            "The envelope lost a field.");
        Require(executions[0].Payload.Count == 0, "A missing payload must become an empty one.");
        Require(Create(new Sender(), true).Epoch != service.Epoch, "Each server run needs its own epoch.");
    }

    private static async Task CheckFailureAndOverloadAsync()
    {
        var sender = new Sender { Throw = true };
        var service = Create(sender, true);
        service.Publish(Draft(ChangeArea.Executions, ChangeEventNames.Started));
        await service.FlushAsync(CancellationToken.None);
        sender.Throw = false;
        service.Publish(Draft(ChangeArea.Executions, ChangeEventNames.Completed));
        await service.FlushAsync(CancellationToken.None);
        Require(sender.Sent.Single().Sequence == 2,
            "A failed send must leave a visible gap in the sequence for the client to detect.");

        // A full queue drops the newest events; the numbers they consumed show up as a gap.
        var flooded = new Sender();
        var overloaded = Create(flooded, true);
        for (var index = 0; index < ChangeEventService.QueueCapacity + 5; index++)
            overloaded.Publish(Draft(ChangeArea.Recommendations, ChangeEventNames.Created));
        await overloaded.FlushAsync(CancellationToken.None);
        overloaded.Publish(Draft(ChangeArea.Recommendations, ChangeEventNames.Created));
        await overloaded.FlushAsync(CancellationToken.None);
        var last = flooded.Sent[^1].Sequence;
        Require(flooded.Sent.Count == ChangeEventService.QueueCapacity + 1 &&
            last == ChangeEventService.QueueCapacity + 6 && flooded.Sent[^2].Sequence == ChangeEventService.QueueCapacity,
            "Overload must drop events visibly (sequence gap), never block or reorder.");
    }

    private static async Task CheckHostedLoopAsync()
    {
        var sender = new Sender();
        using var service = Create(sender, true);
        await service.StartAsync(CancellationToken.None);
        for (var index = 0; index < 20; index++) service.Publish(Draft(ChangeArea.Executions, ChangeEventNames.Progress));
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (sender.Count < 20 && DateTime.UtcNow < deadline) await Task.Delay(20);
        await service.StopAsync(CancellationToken.None);
        Require(sender.Sent.Select(item => item.Sequence).SequenceEqual(Enumerable.Range(1, 20).Select(item => (long)item)),
            "The background loop did not deliver every event in order.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Sender : IChangeEventSender
    {
        private readonly List<ChangeEventEnvelope> sent = [];
        public bool Throw { get; set; }
        public int Count { get { lock (sent) return sent.Count; } }
        public IReadOnlyList<ChangeEventEnvelope> Sent { get { lock (sent) return [.. sent]; } }

        public Task SendAsync(ChangeEventEnvelope envelope, ChangeArea area, CancellationToken cancellationToken)
        {
            if (Throw) throw new InvalidOperationException("Injected transport failure.");
            lock (sent) sent.Add(envelope);
            return Task.CompletedTask;
        }
    }
}
