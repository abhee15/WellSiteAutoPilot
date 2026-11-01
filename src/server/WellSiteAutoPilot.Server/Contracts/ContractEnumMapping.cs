using WellSiteAutoPilot.Failures;
using ContractConfiguredLogicScheduleChangeKind = WellSiteAutoPilot.Api.Contracts.ConfiguredLogic.ConfiguredLogicScheduleChangeKind;
using ContractConfiguredLogicRevisionStatus = WellSiteAutoPilot.Api.Contracts.ConfiguredLogic.ConfiguredLogicRevisionStatus;
using ContractExecutionMode = WellSiteAutoPilot.Api.Contracts.Executions.ExecutionMode;
using ContractExecutionProfile = WellSiteAutoPilot.Api.Contracts.Logic.ExecutionProfile;
using ContractExecutionStatus = WellSiteAutoPilot.Api.Contracts.Executions.ExecutionStatus;
using ContractExecutionTriggerKind = WellSiteAutoPilot.Api.Contracts.Executions.ExecutionTriggerKind;
using ContractGrantScopeKind = WellSiteAutoPilot.Api.Contracts.Security.AccessGrantScopeKind;
using ContractGrantSubjectKind = WellSiteAutoPilot.Api.Contracts.Security.AccessGrantSubjectKind;
using ContractLogicModuleTrustStatus = WellSiteAutoPilot.Api.Contracts.Logic.LogicModuleTrustStatus;
using ContractLogicRuntimeKind = WellSiteAutoPilot.Api.Contracts.Logic.LogicRuntimeKind;
using ContractPlatformComponentStatus = WellSiteAutoPilot.Api.Contracts.System.PlatformComponentStatus;
using ContractRecommendationDecision = WellSiteAutoPilot.Api.Contracts.Recommendations.RecommendationDecision;
using ContractRecommendationStatus = WellSiteAutoPilot.Api.Contracts.Recommendations.RecommendationStatus;
using DomainConfiguredLogicScheduleChangeKind = WellSiteAutoPilot.Domain.ConfiguredLogic.ConfiguredLogicScheduleChangeKind;
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

namespace WellSiteAutoPilot.Server.Contracts;

/// <summary>
/// The contract assembly cannot reference the domain, so each closed API value
/// set is declared there independently and mapped here by member name. Names
/// are the wire values and are unchanged from the former ToString() output; a
/// name missing on either side fails loudly (and is covered by the contract
/// parity checks) rather than emitting or accepting an unlisted value.
/// </summary>
public static class ContractEnumMapping
{
    public static ContractGrantSubjectKind ToContract(this DomainGrantSubjectKind value) => Map<ContractGrantSubjectKind>(value);
    public static ContractGrantScopeKind ToContract(this DomainGrantScopeKind value) => Map<ContractGrantScopeKind>(value);
    public static ContractExecutionMode ToContract(this DomainExecutionMode value) => Map<ContractExecutionMode>(value);
    public static ContractExecutionStatus ToContract(this DomainExecutionStatus value) => Map<ContractExecutionStatus>(value);
    public static ContractExecutionTriggerKind ToContract(this DomainExecutionTriggerKind value) => Map<ContractExecutionTriggerKind>(value);
    public static ContractLogicRuntimeKind ToContract(this DomainLogicRuntimeKind value) => Map<ContractLogicRuntimeKind>(value);
    public static ContractExecutionProfile ToContract(this DomainExecutionProfile value) => Map<ContractExecutionProfile>(value);
    public static ContractLogicModuleTrustStatus ToContract(this DomainLogicModuleTrustStatus value) => Map<ContractLogicModuleTrustStatus>(value);
    public static ContractRecommendationStatus ToContract(this DomainRecommendationStatus value) => Map<ContractRecommendationStatus>(value);
    public static ContractConfiguredLogicRevisionStatus ToContract(this DomainConfiguredLogicRevisionStatus value) => Map<ContractConfiguredLogicRevisionStatus>(value);
    public static ContractConfiguredLogicScheduleChangeKind ToContract(this DomainConfiguredLogicScheduleChangeKind value) => Map<ContractConfiguredLogicScheduleChangeKind>(value);
    public static ContractPlatformComponentStatus ToContract(this DomainPlatformComponentStatus value) => Map<ContractPlatformComponentStatus>(value);

    /// <summary>An omitted or undefined request value is a validation failure, never a default.</summary>
    public static DomainGrantScopeKind ToDomain(this ContractGrantScopeKind value) => MapRequest<DomainGrantScopeKind>(value);

    public static DomainExecutionMode ToDomain(this ContractExecutionMode value) => MapRequest<DomainExecutionMode>(value);

    public static DomainRecommendationStatus ToDomain(this ContractRecommendationDecision value) => MapRequest<DomainRecommendationStatus>(value);

    private static TTo Map<TTo>(Enum value) where TTo : struct, Enum
    {
        var name = value.ToString();
        if (Array.IndexOf(Enum.GetNames<TTo>(), name) < 0)
            throw new InvalidOperationException($"{typeof(TTo).Name} has no member named '{name}'.");
        return Enum.Parse<TTo>(name);
    }

    private static TTo MapRequest<TTo>(Enum value) where TTo : struct, Enum
    {
        var name = value.ToString();
        if (!Enum.IsDefined(value.GetType(), value) || Array.IndexOf(Enum.GetNames<TTo>(), name) < 0)
            throw new WellSiteAutoPilotException(FailureCodes.ValidationFailed, FailureKind.Validation,
                "An explicit supported value is required.");
        return Enum.Parse<TTo>(name);
    }
}
