namespace WellSiteAutoPilot.Domain.Logic;

public enum LogicModuleTrustStatus
{
    Untrusted = 0,
    TrustedPublisher = 1,
    CustomerApproved = 2,
    SignatureInvalid = 3
}
