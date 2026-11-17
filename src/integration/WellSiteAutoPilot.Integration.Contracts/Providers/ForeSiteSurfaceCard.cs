namespace WellSiteAutoPilot.Integration.Contracts.Providers;

/// <summary>A typed stored ForeSite surface card. Position and load share their index.</summary>
public sealed record ForeSiteSurfaceCard(string Id, string Kind, string Representation, string CapturedAtUtc,
    string Source, string PositionUnit, string LoadUnit, IReadOnlyList<decimal> Position, IReadOnlyList<decimal> Load)
{
    public const string Quantity = "SurfaceCards";
    public const string SchemaId = "foresite.surface-cards/v1";
    public const int MaximumCards = 16;
    public const int MaximumPoints = 1024;

    /// <summary>
    /// The JSON Schema a module declares to read these cards. The registry advertises it verbatim so authoring tools never
    /// hand-build a schema that would not match the provider.
    /// </summary>
    public const string JsonSchema = """{"type":"array","items":{"type":"object","properties":{"id":{"type":"string","minLength":1,"maxLength":128},"kind":{"type":"string","minLength":1,"maxLength":128,"enum":["CURRENT","FULL"]},"representation":{"type":"string","minLength":1,"maxLength":128,"enum":["SURFACE"]},"capturedAtUtc":{"type":"string","minLength":1,"maxLength":128},"source":{"type":"string","minLength":1,"maxLength":128},"positionUnit":{"type":"string","minLength":1,"maxLength":128,"enum":["in"]},"loadUnit":{"type":"string","minLength":1,"maxLength":128,"enum":["lb"]},"position":{"type":"array","items":{"type":"number","minimum":-1000000,"maximum":1000000},"minItems":2,"maxItems":1024},"load":{"type":"array","items":{"type":"number","minimum":-1000000,"maximum":1000000},"minItems":2,"maxItems":1024}},"required":["id","kind","representation","capturedAtUtc","source","positionUnit","loadUnit","position","load"],"additionalProperties":false},"minItems":1,"maxItems":16}""";
}
