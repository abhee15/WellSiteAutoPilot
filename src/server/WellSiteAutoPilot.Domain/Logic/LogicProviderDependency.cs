namespace WellSiteAutoPilot.Domain.Logic;

public enum LogicProviderDependencyKind
{
    Data,
    Model,
    Calculation
}

/// <summary>
/// A named provider a module needs (manifest version 3), e.g. role "data" served by adapter "foresite",
/// role "model" by "foresite" and role "calc" by "wami". The role is what a configured logic binds to one connection.
/// </summary>
public sealed record LogicProviderDependency(
    string Role,
    LogicProviderDependencyKind Kind,
    string AdapterKey,
    // Calculation roles only: the provider functions the module may use. Empty or null means none declared.
    IReadOnlyCollection<string>? Functions = null);
