namespace WellSiteAutoPilot.Server.Security;

public sealed class SecurityOptions
{
    public const string SectionName = "Security";
    public const string NegotiateAuthenticationMode = "Negotiate";
    public const string TestHeaderAuthenticationMode = "TestHeader";

    public string AuthenticationMode { get; set; } = NegotiateAuthenticationMode;
    public string[] BootstrapAdministrators { get; set; } = [];
}
