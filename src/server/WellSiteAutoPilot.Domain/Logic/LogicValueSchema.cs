using System.Text.Json;

namespace WellSiteAutoPilot.Domain.Logic;

// Schema identity is module-owned metadata, never a URL to resolve.
public sealed record LogicValueSchema(string SchemaId, JsonElement Schema);

public sealed record LogicOutputRequirement(
    string Id, string Quantity, string? Unit, LogicValueSchema ValueSchema);
