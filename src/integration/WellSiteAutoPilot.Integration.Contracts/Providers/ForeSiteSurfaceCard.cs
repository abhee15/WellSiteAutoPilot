namespace WellSiteAutoPilot.Integration.Contracts.Providers;

/// <summary>A typed stored ForeSite surface card. Position and load share their index.</summary>
public sealed record ForeSiteSurfaceCard(string Id, string Kind, string Representation, string CapturedAtUtc,
    string Source, string PositionUnit, string LoadUnit, IReadOnlyList<decimal> Position, IReadOnlyList<decimal> Load)
{
    public const string Quantity = "SurfaceCards";
    public const string SchemaId = "foresite.surface-cards/v1";
    public const int MaximumCards = 16;
    public const int MaximumPoints = 1024;
}
