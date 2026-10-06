namespace WellSiteAutoPilot.Domain.Logic;

public enum ExecutionProfile
{
    ScheduledOneShot = 0,
    BoundedIterative = 1,
    Reactive = 2,
    ContinuousController = 3,
    DurableWorkflow = 4
}
