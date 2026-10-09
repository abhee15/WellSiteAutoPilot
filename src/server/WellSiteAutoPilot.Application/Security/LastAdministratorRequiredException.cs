namespace WellSiteAutoPilot.Application.Security;

public sealed class LastAdministratorRequiredException()
    : InvalidOperationException(
        "At least one active WellSite AutoPilot administrator is required.");
