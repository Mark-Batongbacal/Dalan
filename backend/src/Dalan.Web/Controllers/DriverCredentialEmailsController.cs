using Dalan.Infrastructure.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dalan.Web.Controllers;

// Read-only operational status. Provider acceptance is not proof of inbox receipt.
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/driver-credential-emails")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DriverCredentialEmailsController(
    IDriverCredentialEmailOutbox outbox, IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Recent(CancellationToken cancellationToken)
    {
        if (!bool.TryParse(configuration["DriverCredentialEmail:Enabled"], out var enabled) || !enabled)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Driver credential email delivery has not been enabled." });

        return Ok(await outbox.RecentAsync(cancellationToken));
    }
}
