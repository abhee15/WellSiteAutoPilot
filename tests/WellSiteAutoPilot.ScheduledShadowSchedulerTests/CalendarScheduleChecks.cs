using WellSiteAutoPilot.Application.Scheduling;
using WellSiteAutoPilot.Domain.ConfiguredLogic;

internal static class CalendarScheduleChecks
{
    public static void Run()
    {
        var start = DateTimeOffset.Parse("2027-01-31T14:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var monthly = new ConfiguredLogicSchedule(true, 28 * 86400, start, "America/Chicago")
        { Recurrence = new(1, "Months") };
        ScheduleOccurrences.Validate(monthly);
        var february = DateTimeOffset.Parse("2027-02-28T14:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var march = DateTimeOffset.Parse("2027-03-31T13:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        Require(ScheduleOccurrences.NextAfter(monthly, start) == february, "Monthly short-day clamp");
        Require(ScheduleOccurrences.NextAfter(monthly, february) == march, "Monthly anchor and DST");
        var resumed = monthly with { ResumeAfterUtc = february.AddDays(1) };
        Require(ScheduleOccurrences.LatestDue(resumed, february.AddDays(2)) is null, "Resume skips missed calendar runs");
        Require(ScheduleOccurrences.NextAfter(resumed, february) == march, "Resume preserves original month day");
        Require(ScheduleOccurrences.LatestDue(resumed, march) == march, "Resumed calendar dispatch");
        Require(ScheduleOccurrences.LatestDue(monthly, march.AddHours(1)) == march, "Latest calendar occurrence");
        Require(ScheduleOccurrences.NextAfter(monthly with { Recurrence = new(1, "Months", true) }, start) == march, "Skip missing day");
        Require(ScheduleOccurrences.NextAfter(monthly with { EndAtUtc = february }, february) is null, "End boundary");
        Require(ScheduleOccurrences.LatestDue(monthly with { EndAtUtc = february }, february.AddSeconds(1)) is null, "Ended dispatch");

        var daily = new ConfiguredLogicSchedule(true, 86400, DateTimeOffset.Parse("2027-03-13T08:30:00Z", System.Globalization.CultureInfo.InvariantCulture), "America/Chicago")
        { Recurrence = new(1, "Days") };
        Require(ScheduleOccurrences.NextAfter(daily, daily.StartAtUtc) == DateTimeOffset.Parse("2027-03-15T07:30:00Z", System.Globalization.CultureInfo.InvariantCulture), "Skip missing clock hour");
        var autumn = daily with { StartAtUtc = DateTimeOffset.Parse("2027-11-06T06:30:00Z", System.Globalization.CultureInfo.InvariantCulture) };
        var repeated = DateTimeOffset.Parse("2027-11-07T06:30:00Z", System.Globalization.CultureInfo.InvariantCulture);
        Require(ScheduleOccurrences.NextAfter(autumn, autumn.StartAtUtc) == repeated, "Repeated hour uses earlier offset");
        Require(ScheduleOccurrences.NextAfter(autumn, repeated) == DateTimeOffset.Parse("2027-11-08T07:30:00Z", System.Globalization.CultureInfo.InvariantCulture), "Repeated hour executes once");
    }

    private static void Require(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
    }
}
