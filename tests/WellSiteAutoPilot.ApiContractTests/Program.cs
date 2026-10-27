using System.Text.Json;
using WellSiteAutoPilot.Api.Contracts.Executions;
using WellSiteAutoPilot.Api.Contracts.Recommendations;
using WellSiteAutoPilot.Api.Contracts.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Server.Contracts;
using ContractConfiguredLogicRevisionStatus = WellSiteAutoPilot.Api.Contracts.ConfiguredLogic.ConfiguredLogicRevisionStatus;
using ContractExecutionProfile = WellSiteAutoPilot.Api.Contracts.Logic.ExecutionProfile;
using ContractLogicModuleTrustStatus = WellSiteAutoPilot.Api.Contracts.Logic.LogicModuleTrustStatus;
using ContractLogicRuntimeKind = WellSiteAutoPilot.Api.Contracts.Logic.LogicRuntimeKind;
using ContractPlatformComponentStatus = WellSiteAutoPilot.Api.Contracts.System.PlatformComponentStatus;
using DomainConfiguredLogicRevisionStatus = WellSiteAutoPilot.Domain.ConfiguredLogic.ConfiguredLogicRevisionStatus;
using DomainExecutionMode = WellSiteAutoPilot.Domain.Executions.ExecutionMode;
using DomainExecutionProfile = WellSiteAutoPilot.Domain.Logic.ExecutionProfile;
using DomainExecutionStatus = WellSiteAutoPilot.Domain.Executions.ExecutionStatus;
using DomainExecutionTriggerKind = WellSiteAutoPilot.Domain.Executions.ExecutionTriggerKind;
using DomainGrantScopeKind = WellSiteAutoPilot.Domain.Security.AccessGrantScopeKind;
using DomainGrantSubjectKind = WellSiteAutoPilot.Domain.Security.AccessGrantSubjectKind;
using DomainLogicModuleTrustStatus = WellSiteAutoPilot.Domain.Logic.LogicModuleTrustStatus;
using DomainLogicRuntimeKind = WellSiteAutoPilot.Domain.Logic.LogicRuntimeKind;
using DomainPlatformComponentStatus = WellSiteAutoPilot.Domain.System.PlatformComponentStatus;
using DomainRecommendationStatus = WellSiteAutoPilot.Domain.Recommendations.RecommendationStatus;

// ASP.NET minimal APIs read and write bodies with the web defaults.
ExecutionProvenanceChecks.Run();

var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);

// Every domain value maps to the contract member of the same name, so the
// wire strings are exactly what ToString() produced before the contract was
// typed, and the contract declares nothing the domain does not.
Parity<DomainGrantSubjectKind, AccessGrantSubjectKind>(value => value.ToContract());
Parity<DomainGrantScopeKind, AccessGrantScopeKind>(value => value.ToContract());
Parity<DomainExecutionMode, ExecutionMode>(value => value.ToContract());
Parity<DomainExecutionStatus, ExecutionStatus>(value => value.ToContract());
Parity<DomainExecutionTriggerKind, ExecutionTriggerKind>(value => value.ToContract());
Parity<DomainLogicRuntimeKind, ContractLogicRuntimeKind>(value => value.ToContract());
Parity<DomainExecutionProfile, ContractExecutionProfile>(value => value.ToContract());
Parity<DomainLogicModuleTrustStatus, ContractLogicModuleTrustStatus>(value => value.ToContract());
Parity<DomainRecommendationStatus, RecommendationStatus>(value => value.ToContract());
Parity<DomainConfiguredLogicRevisionStatus, ContractConfiguredLogicRevisionStatus>(value => value.ToContract());
Parity<DomainPlatformComponentStatus, ContractPlatformComponentStatus>(value => value.ToContract());

// Pinned literals guard the documented wire values themselves.
ExpectJson(AccessGrantSubjectKind.WindowsGroup, "\"WindowsGroup\"");
ExpectJson(AccessGrantScopeKind.DeploymentWide, "\"DeploymentWide\"");
ExpectJson(ExecutionStatus.TimedOut, "\"TimedOut\"");
ExpectJson(ContractLogicRuntimeKind.DotNet, "\"DotNet\"");
ExpectJson(ContractExecutionProfile.ScheduledOneShot, "\"ScheduledOneShot\"");
ExpectJson(ContractLogicModuleTrustStatus.CustomerApproved, "\"CustomerApproved\"");

// Unknown, malformed, numeric, differently cased, padded, null and non-string
// values must never deserialize, for every converted enum.
foreach (var type in new[]
{
    typeof(AccessGrantSubjectKind), typeof(AccessGrantScopeKind), typeof(ExecutionMode),
    typeof(ExecutionStatus), typeof(ExecutionTriggerKind), typeof(ContractLogicRuntimeKind),
    typeof(ContractExecutionProfile), typeof(ContractLogicModuleTrustStatus), typeof(RecommendationStatus),
    typeof(RecommendationDecision), typeof(ContractConfiguredLogicRevisionStatus),
    typeof(ContractPlatformComponentStatus)
})
{
    var first = Enum.GetNames(type)[0];
    foreach (var bad in new[]
    {
        "\"NotAValue\"", "\"\"", "0", "1", "99", "-1", "1.0", "\"0\"", "\"1\"", "null", "true", "{}", "[]",
        "\"" + first.ToLowerInvariant() + "\"", "\"" + first.ToUpperInvariant() + "\"",
        "\"" + first + " \"", "\" " + first + "\"", "\"" + first + ",ExactAssets\""
    })
    {
        ExpectRejected(type, bad);
    }
    // An undefined member is never written as a name or a number.
    ExpectThrows<JsonException>(() => JsonSerializer.Serialize(Enum.ToObject(type, 0), type, web),
        type.Name + " must not serialize an undefined member");
    ExpectThrows<JsonException>(() => JsonSerializer.Serialize(Enum.ToObject(type, 99), type, web),
        type.Name + " must not serialize an undefined member");
}

// Request bodies: the typed property keeps the previous fail-closed behavior.
var create = JsonSerializer.Deserialize<CreateAccessGrantRequest>(
    """{"roleId":"11111111-1111-1111-1111-111111111111","scopeKind":"AllAssets","expectedRoleRevision":3}""", web)
    ?? throw new InvalidOperationException("Valid grant request must deserialize.");
Expect(create.ScopeKind == AccessGrantScopeKind.AllAssets && create.ScopeKind.ToDomain() == DomainGrantScopeKind.AllAssets,
    "Grant scope must map by name");
foreach (var bad in new[] { "\"Everything\"", "1", "\"1\"", "\"allassets\"", "null" })
{
    ExpectThrows<JsonException>(() => JsonSerializer.Deserialize<CreateAccessGrantRequest>(
        "{\"roleId\":\"11111111-1111-1111-1111-111111111111\",\"scopeKind\":" + bad + ",\"expectedRoleRevision\":3}", web),
        "Grant request with scopeKind " + bad + " must be rejected");
}
var omitted = JsonSerializer.Deserialize<CreateAccessGrantRequest>(
    """{"roleId":"11111111-1111-1111-1111-111111111111","expectedRoleRevision":3}""", web)!;
ExpectThrows<WellSiteAutoPilotException>(() => omitted.ScopeKind.ToDomain(),
    "An omitted scopeKind must be a validation failure, not the first member");
ExpectThrows<WellSiteAutoPilotException>(() => ((AccessGrantScopeKind)99).ToDomain(),
    "An undefined scope must be a validation failure");

var decision = JsonSerializer.Deserialize<RecommendationDecisionRequest>(
    """{"decision":"Rejected","reason":"Not safe"}""", web)!;
Expect(decision.Decision.ToDomain() == DomainRecommendationStatus.Rejected, "Decision must map by name");
foreach (var notADecision in new[] { "Pending", "Executed", "Created", "approved", "2" })
{
    ExpectThrows<JsonException>(() => JsonSerializer.Deserialize<RecommendationDecisionRequest>(
        "{\"decision\":" + (notADecision == "2" ? notADecision : "\"" + notADecision + "\"") + "}", web),
        "Decision " + notADecision + " must be rejected");
}
ExpectThrows<WellSiteAutoPilotException>(
    () => JsonSerializer.Deserialize<RecommendationDecisionRequest>("""{"reason":"x"}""", web)!.Decision.ToDomain(),
    "An omitted decision must be a validation failure");

// Responses keep the existing string wire format.
var grantJson = JsonSerializer.Serialize(new AccessGrantResponse(
    Guid.Empty, AccessGrantSubjectKind.WindowsGroup, null, "S-1-5-21-1", Guid.Empty,
    AccessGrantScopeKind.DeploymentWide, [], true, 1), web);
using (var document = JsonDocument.Parse(grantJson))
{
    var root = document.RootElement;
    Expect(root.GetProperty("subjectKind").GetString() == "WindowsGroup" &&
        root.GetProperty("scopeKind").GetString() == "DeploymentWide",
        "Grant response wire values changed: " + grantJson);
}

Console.WriteLine("API closed-value contract checks passed.");
return 0;

void Parity<TDomain, TContract>(Func<TDomain, TContract> map)
    where TDomain : struct, Enum where TContract : struct, Enum
{
    var domainNames = Enum.GetNames<TDomain>().Order(StringComparer.Ordinal).ToArray();
    var contractNames = Enum.GetNames<TContract>().Order(StringComparer.Ordinal).ToArray();
    Expect(domainNames.SequenceEqual(contractNames),
        typeof(TContract).Name + " members differ from the domain: " + string.Join(",", contractNames));
    foreach (var value in Enum.GetValues<TDomain>())
    {
        var mapped = map(value);
        Expect(mapped.ToString() == value.ToString(), typeof(TContract).Name + " mapping changed " + value);
        Expect(JsonSerializer.Serialize(mapped, web) == "\"" + value + "\"",
            typeof(TContract).Name + " wire value changed for " + value);
        Expect(JsonSerializer.Deserialize<TContract>("\"" + value + "\"", web).Equals(mapped),
            typeof(TContract).Name + " did not round trip " + value);
        Expect(Convert.ToInt32(mapped, System.Globalization.CultureInfo.InvariantCulture) != 0,
            typeof(TContract).Name + " must leave 0 undefined");
    }
    ExpectThrows<InvalidOperationException>(() => map((TDomain)Enum.ToObject(typeof(TDomain), 99)),
        typeof(TDomain).Name + " undefined value must not map");
}

void ExpectJson<T>(T value, string expected) where T : struct, Enum =>
    Expect(JsonSerializer.Serialize(value, web) == expected, typeof(T).Name + " wire value changed");

void ExpectRejected(Type type, string json) =>
    ExpectThrows<JsonException>(() => JsonSerializer.Deserialize(json, type, web), type.Name + " must reject " + json);

void ExpectThrows<TException>(Action action, string message) where TException : Exception
{
    try { action(); }
    catch (TException) { return; }
    throw new InvalidOperationException(message);
}

void Expect(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
