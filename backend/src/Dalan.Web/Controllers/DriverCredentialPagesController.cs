using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Dalan.Web.Controllers;
[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DriverCredentialPagesController : Controller
{
    [HttpGet("/DriverInvitations/Activate")]
    public IActionResult Activate() => Page("Activate your Driver account", "/api/driver-invitations/activate");

    [HttpGet("/DriverRecovery/Request")]
    public IActionResult RequestReset()
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return View("~/Views/Account/DriverRecoveryRequest.cshtml");
    }

    [HttpGet("/DriverRecovery/Reset")]
    public IActionResult Reset() => Page("Reset your Driver password", "/api/driver-recovery/reset");

    private IActionResult Page(string title, string endpoint)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        ViewData["Title"] = title;
        ViewData["Endpoint"] = endpoint;
        return View("~/Views/Account/DriverCredential.cshtml");
    }
}
