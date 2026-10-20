using System.Security.Principal;
using CygNet.API.Core;
using CygNet.Data.Core;
using Microsoft.AspNetCore.Authentication.Negotiate;

// CygNet Native Agent (POC). Read-only. Mirrors ForeSite's CygNetFacilityHelper.GetWellFacilities:
// resolve the FAC service associated with the domain/site/service, query facility tags, read each record.
// Runs as the caller (Windows auth + impersonation) so CygNet ACS permissions apply per user.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
builder.Services.AddAuthorization(o => o.FallbackPolicy = o.DefaultPolicy);
var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

// Health: the agent is up. 503 when the CygNet client libraries cannot find an ambient domain.
app.MapGet("/health", () =>
{
    try
    {
        var domain = new ServiceInformation().GetAmbientDomain();
        return Results.Ok(new { status = "ok", ambientDomain = domain });
    }
    catch (Exception ex)
    {
        return Results.Json(new { status = "cygnet-unavailable", error = ex.Message }, statusCode: 503);
    }
});

app.MapGet("/facilities", (HttpContext http, ushort domain, string site, string service, string? type) =>
{
    if (domain == 0 || string.IsNullOrWhiteSpace(site) || string.IsNullOrWhiteSpace(service))
    {
        return Results.BadRequest(new { error = "domain, site and service are required." });
    }
    if (http.User.Identity is not WindowsIdentity caller)
    {
        return Results.Unauthorized();
    }

    var facilityType = string.IsNullOrWhiteSpace(type) ? "WELL" : type;
    try
    {
        return WindowsIdentity.RunImpersonated(caller.AccessToken, () => ReadFacilities(domain, site, service, facilityType));
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 502);
    }
});

app.Run();

static IResult ReadFacilities(ushort domain, string site, string service, string facilityType)
{
    var info = new ServiceInformation();
    var domainSiteService = new DomainSiteService(domain, new SiteService(site, service));
    var fac = info.GetAssociatedService(domainSiteService, ServiceType.FAC);
    var client = new CygNet.API.Facilities.Client(fac);

    // Facility attributes are not interpolated from user text without quoting: reject quotes outright.
    if (site.Contains('\'') || service.Contains('\'') || facilityType.Contains('\''))
    {
        return Results.BadRequest(new { error = "Invalid character in site, service or type." });
    }
    var query = $"facility_site = '{site}' AND facility_type = '{facilityType}' AND facility_service = '{service}'";
    var tags = client.GetFacilityTagList(query, default);

    var results = new System.Collections.Concurrent.ConcurrentBag<object>();
    var readFailed = 0;
    Parallel.ForEach(tags, tag =>
    {
        try
        {
            var record = client.ReadFacility(tag);
            results.Add(new
            {
                site = tag.Site,
                service = tag.Service,
                siteService = tag.SiteService.ToString(),
                facilityId = tag.FacilityId,
                facilityType = record.Type,
                facilityDescription = record.Description
            });
        }
        catch (System.Security.SecurityException)
        {
            // Component-level security: this user may read only a subset of the FAC service.
            Interlocked.Exchange(ref readFailed, 1);
        }
    });

    return Results.Ok(new { readFailed = readFailed == 1, facilities = results.ToArray() });
}
