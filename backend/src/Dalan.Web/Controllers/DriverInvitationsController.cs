using Dalan.Application.Identity;
using Dalan.Contracts.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Dalan.Web.Controllers;
[ApiController]
[Route("api/driver-invitations")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DriverInvitationsController(IDriverInvitationsService service) : ControllerBase
{
    [HttpPost("issue")]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Issue(DriverInvitationIssueRequest request, CancellationToken cancellationToken)
    {
        var result = await service.Issue(request, cancellationToken);
        return result.Value is null ? StatusCode(result.StatusCode) : StatusCode(result.StatusCode, result.Value);
    }
    [HttpPost("activate")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Activate(DriverInvitationActivationRequest request, CancellationToken cancellationToken)
    {
        var result = await service.Activate(request, cancellationToken);
        return result.Value is null ? StatusCode(result.StatusCode) : StatusCode(result.StatusCode, result.Value);
    }
}
