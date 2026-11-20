using Dalan.Application.Identity;
using Dalan.Contracts.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace Dalan.Web.Controllers;
[ApiController]
[Route("api/driver-recovery")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[AllowAnonymous]
[EnableRateLimiting("driver-recovery")]
public sealed class DriverRecoveryController(IDriverRecoveryService service) : ControllerBase
{
    [HttpPost("request")]
    public async Task<IActionResult> RequestReset(DriverRecoveryEmailRequest request, CancellationToken cancellationToken)
    {
        var result = await service.RequestReset(request, cancellationToken);
        return result.Value is null ? StatusCode(result.StatusCode) : StatusCode(result.StatusCode, result.Value);
    }
    [HttpPost("reset")]
    public async Task<IActionResult> Reset(DriverRecoveryPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await service.Reset(request, cancellationToken);
        return result.Value is null ? StatusCode(result.StatusCode) : StatusCode(result.StatusCode, result.Value);
    }
}
