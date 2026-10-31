using System.Reflection;

namespace WellSiteAutoPilot.ScheduleManagementTests;

/// <summary>Implements an interface by routing named calls to handlers, so a check fakes only what it needs.</summary>
public class StubProxy : DispatchProxy
{
    internal Dictionary<string, Func<object?[], object?>> Handlers { get; } = [];

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        Handlers.TryGetValue(targetMethod!.Name, out var handler)
            ? handler(args ?? [])
            : throw new NotImplementedException($"{targetMethod.DeclaringType?.Name}.{targetMethod.Name} was not expected.");
}

public static class Stub
{
    public static T Of<T>(params (string Method, Func<object?[], object?> Handler)[] methods) where T : class
    {
        var proxy = DispatchProxy.Create<T, StubProxy>();
        foreach (var (method, handler) in methods)
            ((StubProxy)(object)proxy).Handlers[method] = handler;
        return proxy;
    }
}
