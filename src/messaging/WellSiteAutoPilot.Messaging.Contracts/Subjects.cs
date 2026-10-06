namespace WellSiteAutoPilot.Messaging.Contracts;

public static class Subjects
{
    public const string ExecutionRequestedV1 = "wsa.execution.requested.v1";
    public const string ExecutionCompletedV1 = "wsa.execution.completed.v1";
    public const string ExecutionFailedV1 = "wsa.execution.failed.v1";

    public const string ControlActionRequestedV1 = "wsa.control.action.requested.v1";
    public const string ControlActionCompletedV1 = "wsa.control.action.completed.v1";
    public const string ControlActionFailedV1 = "wsa.control.action.failed.v1";

    public const string IntegrationHealthChangedV1 = "wsa.integration.health.changed.v1";
}
