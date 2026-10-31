using System.Security.Cryptography;
using System.Text.Json;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Application.Connections.Importing;

public sealed record SourceFactDifference(string Field, string? Accepted, string? Observed);
public sealed record SourceComparison(string Status, IReadOnlyList<SourceFactDifference> Differences,
    IReadOnlyList<string> UnknownFields, string Fingerprint);

/// <summary>Conservative source comparison. Missing facts never mean a confirmed blank.</summary>
public static class SourceChangeComparison
{
    private static readonly string[] ForeSiteFacts = ["wellType", "wellTypeId", "deviceType"];
    public static SourceComparison Compare(string? acceptedJson, DiscoveredAsset? observed, bool complete,
        IReadOnlyList<string>? unavailable)
    {
        var baseline = acceptedJson is null ? null : ImportedSourceBaseline.Read(acceptedJson);
        var differences = new List<SourceFactDifference>();
        var unknown = new HashSet<string>(StringComparer.Ordinal);
        string status;
        if (observed is null) status = complete ? "NotReturned" : "ComparisonUnavailable";
        else if (acceptedJson is null) status = "New";
        else if (baseline is null) status = "ComparisonUnavailable";
        else
        {
            var foresite = observed.ProviderType == "foresite.well";
            if (baseline.SourceName is null || foresite &&
                (!baseline.Facts.ContainsKey("wellName") || !observed.Metadata.ContainsKey("wellName"))) unknown.Add("sourceName");
            else if (Clean(baseline.SourceName) != Clean(observed.DisplayName))
                differences.Add(new("sourceName", baseline.SourceName, observed.DisplayName));
            foreach (var key in baseline.Facts.Keys.Union(observed.Metadata.Keys).Union(foresite ? ForeSiteFacts : []).Order(StringComparer.Ordinal))
            {
                var hadBefore = baseline.Facts.TryGetValue(key, out var before);
                var hasAfter = observed.Metadata.TryGetValue(key, out var after);
                if (unavailable is null || unavailable.Contains(key) || baseline.UnavailableFields?.Contains(key) == true ||
                    !hadBefore || !hasAfter || foresite && (string.IsNullOrWhiteSpace(before) || string.IsNullOrWhiteSpace(after)))
                { unknown.Add(key); continue; }
                if (Clean(before) != Clean(after)) differences.Add(new(key, before, after));
            }
            status = differences.Count > 0 ? "Changed" : unknown.Count > 0 ? "ComparisonUnavailable" : "Unchanged";
        }
        // Include the evidence, not provider JSON ordering or incidental whitespace, in the deduplication key.
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
            status, accepted = baseline is null ? null : new { name = Clean(baseline.SourceName), facts = Sorted(baseline.Facts) },
            observed = observed is null ? null : new { name = Clean(observed.DisplayName), facts = Sorted(observed.Metadata) },
            unknown = unknown.Order(StringComparer.Ordinal).ToArray()
        })));
        return new(status, differences, unknown.Order(StringComparer.Ordinal).ToArray(), fingerprint);
    }

    private static string? Clean(string? value) => value is null ? null : string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static object[] Sorted(IReadOnlyDictionary<string, string> facts) => facts.OrderBy(pair => pair.Key, StringComparer.Ordinal)
        .Select(pair => (object)new { pair.Key, value = Clean(pair.Value) }).ToArray();
}

public sealed record SourceCheckSummary(Guid Id, int Revision, int ConnectionRevision, string Status,
    DateTimeOffset StartedAtUtc, DateTimeOffset? CompletedAtUtc, string? ActingAs, int Found, int Changed,
    int New, int NotReturned, int ComparisonUnavailable, IReadOnlyList<string> Warnings,
    IReadOnlyList<string>? UnavailableFields, int? SourceScopeCount, string? SourceScopeFingerprint);
public sealed record SourceReference(Guid Id, string Name, Guid? RevisionId, int? RevisionNumber, string Status,
    string Kind, string Reference);
public sealed record SourceChangeItem(Guid Id, Guid? AssetId, string Kind, string ExternalId, string Name,
    string Status, DateTimeOffset FirstSeenAtUtc, DateTimeOffset LastSeenAtUtc, SourceComparison Evidence,
    IReadOnlyList<SourceReference> Views, IReadOnlyList<SourceReference> Configurations, bool ReferencesTruncated);
public sealed record SourceChangePage(SourceCheckSummary? LatestCheck, SourceCheckSummary? LastSuccessfulCheck,
    int Total, int Offset, IReadOnlyList<SourceChangeItem> Items);

public interface ISourceChangeRepository
{
    Task<SourceCheckSummary> BeginAsync(Guid connectionId, int expectedRevision, AccessSubject subject,
        SecurityActorContext actor, CancellationToken cancellationToken);
    Task<SourceCheckSummary> CompleteAsync(Guid connectionId, Guid checkId, DiscoveryResult? result,
        AccessSubject subject, SecurityActorContext actor, CancellationToken cancellationToken);
    Task<SourceChangePage> ReadAsync(Guid connectionId, int offset, int limit, string? query,
        AccessSubject subject, CancellationToken cancellationToken);
}

public sealed class SourceChangeService(ConnectionService connections, ISourceChangeRepository repository)
{
    public async Task<SourceCheckSummary> CheckAsync(Guid connectionId, AccessSubject subject, SecurityActorContext actor,
        CancellationToken cancellationToken = default)
    {
        var connection = await connections.GetAsync(connectionId, cancellationToken);
        var check = await repository.BeginAsync(connectionId, connection.Revision, subject, actor, cancellationToken);
        DiscoveryResult result;
        try { result = await connections.DiscoverAsync(connectionId, subject, actor, cancellationToken); }
        catch (Exception)
        {
            // A disconnected browser must not leave an apparently successful check. Persist no provider exception text.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await repository.CompleteAsync(connectionId, check.Id, null, subject, actor, timeout.Token);
            throw;
        }
        using var completionTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        return await repository.CompleteAsync(connectionId, check.Id, result, subject, actor, completionTimeout.Token);
    }

    public Task<SourceChangePage> ReadAsync(Guid connectionId, int offset, int limit, string? query, AccessSubject subject,
        CancellationToken cancellationToken = default) => repository.ReadAsync(connectionId, Math.Max(0, offset),
            Math.Clamp(limit, 1, 100), query?.Trim(), subject, cancellationToken);
}
