using WellSiteAutoPilot.Application.Control;
using WellSiteAutoPilot.Domain.Control;

internal static class ModuleCommandDeclarationChecks
{
    private const string Manifest = """
        {"moduleId":"a.b","commandRequirements":[
          {"id":"speed","assetRole":"well","command":"SetSpeed","quantity":"PumpingSpeed","canonicalUnit":"spm"},
          {"id":"stop","assetRole":"well","command":"StopPump","quantity":"RunState"}]}
        """;

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Run()
    {
        var declared = ModuleCommandDeclarations.Read(Manifest)
            ?? throw new InvalidOperationException("A well-formed manifest must be read.");
        Expect(declared.Count == 2 && declared[0].Command == "SetSpeed" && declared[0].CanonicalUnit == "spm" &&
               declared[1].Command == "StopPump" && declared[1].CanonicalUnit is null, "The declared commands were not read as written.");
        Expect(ModuleCommandDeclarations.Read("""{"CommandRequirements":[{"Command":"X"}]}""")?.Count == 1,
            "Property names are read regardless of case.");
        Expect(ModuleCommandDeclarations.Read("""{"commandRequirements":[]}""")?.Count == 0,
            "A module that declares nothing is not an unreadable one.");
        foreach (var unreadable in new[] { "", "   ", "not json", "[]", "{}", """{"commandRequirements":{}}""",
                     """{"commandRequirements":[7]}""", """{"commandRequirements":[{"id":"x"}]}""",
                     """{"commandRequirements":[{"command":" "}]}""", """{"commandRequirements":[{"command":3}]}""" })
            Expect(ModuleCommandDeclarations.Read(unreadable) is null, $"'{unreadable}' must be reported as unreadable.");

        AutonomyCommandLimit Limit(string command, string? unit = "spm") => new(command, unit, 1m, 2m);
        Expect(ModuleCommandDeclarations.Check([Limit("SetSpeed")], declared).Count == 0, "A declared command with its unit must pass.");
        Expect(ModuleCommandDeclarations.Check([Limit("SetSpeed", null)], declared).Count == 0, "A limit with no unit must pass.");
        Expect(ModuleCommandDeclarations.Check([Limit("StopPump", "anything")], declared).Count == 0,
            "A command whose module declares no unit accepts any stated unit.");
        Expect(ModuleCommandDeclarations.Check([Limit("setspeed")], declared).Count == 1, "A command with the wrong case must be refused.");
        Expect(ModuleCommandDeclarations.Check([Limit("SetChoke")], declared).Count == 1, "An undeclared command must be refused.");
        Expect(ModuleCommandDeclarations.Check([Limit("SetSpeed", "psi")], declared).Count == 1, "A different unit must be refused.");
        Expect(ModuleCommandDeclarations.Check([Limit("SetChoke"), Limit("SetSpeed", "psi")], declared).Count == 2,
            "Every problem must be reported.");
        Expect(ModuleCommandDeclarations.Check([Limit("SetSpeed")], []).Single().Contains("declares no commands", StringComparison.Ordinal),
            "A module with no commands must say so.");
    }
}
