using Dalan.Application.Drivers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Dalan.Web.Controllers;
[Authorize(Roles = "Admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DriverAdministrationController(IDriverService drivers) : Controller
{
    [HttpGet("/Drivers/{id:guid}/Access")]
    public async Task<IActionResult> Access(Guid id, CancellationToken cancellationToken)
    {
        var driver = await drivers.GetByIdAsync(id, cancellationToken);
        return driver is null ? NotFound() : View(driver);
    }
    [HttpGet("/AccountAudit")]
    public IActionResult Audit() => View();
    [HttpGet("/DriverCredentialEmails")]
    public IActionResult Emails() => View();
}
