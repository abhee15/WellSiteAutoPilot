using WellSiteAutoPilot.AuthoringTests;

ExpressionChecks.Run();
await DraftChecks.RunAsync();
await LiveChecks.RunAsync();
Console.WriteLine("Authoring checks passed.");
