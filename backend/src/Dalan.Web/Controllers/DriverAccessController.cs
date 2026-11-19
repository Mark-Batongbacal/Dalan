using System.Security.Claims;
using Dalan.Application.Identity;
using Dalan.Contracts.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Dalan.Web.Controllers;
[ApiController]
[Route("api/admin/driver-mobile-access")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Authorize(Roles = "Admin")]
public sealed class DriverAccessController(IDriverAccessService service) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Status(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.Status(id, cancellationToken);
        return result.Value is null ? StatusCode(result.StatusCode) : StatusCode(result.StatusCode, result.Value);
    }
    [HttpPost("{id:guid}/disable")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Disable(Guid id, DriverAccessChangeRequest request, CancellationToken cancellationToken)
    {
        var result = await service.Disable(id, request, User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown", cancellationToken);
        return result.Value is null ? StatusCode(result.StatusCode) : StatusCode(result.StatusCode, result.Value);
    }
    [HttpPost("{id:guid}/enable")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Enable(Guid id, DriverAccessChangeRequest request, CancellationToken cancellationToken)
    {
        var result = await service.Enable(id, request, User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown", cancellationToken);
        return result.Value is null ? StatusCode(result.StatusCode) : StatusCode(result.StatusCode, result.Value);
    }
}
