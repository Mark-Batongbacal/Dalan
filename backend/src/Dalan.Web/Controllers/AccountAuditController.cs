using Dalan.Application.Identity;
using Dalan.Contracts.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Dalan.Web.Controllers;
[ApiController]
[Route("api/admin/account-audit")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Authorize(Roles = "Admin")]
public sealed class AccountAuditController(IAccountAuditService service) : ControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> List(Guid? driverId, DateTimeOffset? beforeUtc, int take = 50, CancellationToken cancellationToken = default, Guid? beforeId = null)
    {
        var result = await service.List(driverId, beforeUtc, take, cancellationToken, beforeId);
        return result.Value is null ? StatusCode(result.StatusCode) : StatusCode(result.StatusCode, result.Value);
    }
}
