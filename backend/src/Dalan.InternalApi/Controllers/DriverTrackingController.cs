using Dalan.Application.DriverExecution;
using Dalan.Application.Tracking;
using Dalan.Contracts.Tracking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Dalan.InternalApi.Controllers;

[ApiController]
[Route("api/driver/tracking")]
[Authorize(AuthenticationSchemes = "DriverBearer", Roles = "Driver")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DriverTrackingController(SignInManager<IdentityUser> signIn, UserManager<IdentityUser> users,
    IDriverTasks tasks, IFleetTrackingService tracking, TrackingOptions options) : ControllerBase
{
    [HttpPost("start")]
    public async Task<IActionResult> Start(CancellationToken cancellationToken)
    {
        if (await Driver(cancellationToken) is not Guid driverId) return Unauthorized();
        if (!options.Enabled) return StatusCode(503, new { message = "Fleet tracking has not been enabled." });
        var session = await tracking.StartAsync(driverId, cancellationToken);
        return session is null ? Conflict(new { message = "An active delivery and current vehicle assignment are required." })
            : Ok(new TrackingSessionResponse(session.SessionId, session.VehicleId));
    }

    [HttpPost("{sessionId:guid}/samples"), RequestSizeLimit(4096)]
    public async Task<IActionResult> Sample(Guid sessionId, TrackingFixRequest request, CancellationToken cancellationToken)
    {
        if (await Driver(cancellationToken) is not Guid driverId) return Unauthorized();
        if (!options.Enabled) return StatusCode(503);
        var fix = new TrackingFix(request.SampleId, request.CapturedAtUtc, request.Latitude, request.Longitude, request.AccuracyMeters);
        return await tracking.RecordAsync(driverId, sessionId, fix, cancellationToken) switch
        {
            TrackingWriteStatus.Accepted or TrackingWriteStatus.Duplicate => Ok(new { continueTracking = true }),
            TrackingWriteStatus.Stopped => Ok(new { continueTracking = false }),
            TrackingWriteStatus.Missing => NotFound(),
            TrackingWriteStatus.Conflict => Conflict(),
            TrackingWriteStatus.TooFrequent => StatusCode(429),
            _ => BadRequest(new { message = "A fresh, accurate, valid GPS sample is required." })
        };
    }

    [HttpPost("{sessionId:guid}/stop")]
    public async Task<IActionResult> Stop(Guid sessionId, CancellationToken cancellationToken)
    {
        if (await Driver(cancellationToken) is not Guid driverId) return Unauthorized();
        await tracking.StopAsync(driverId, sessionId, cancellationToken);
        return NoContent();
    }

    private async Task<Guid?> Driver(CancellationToken cancellationToken)
    {
        var user = await signIn.ValidateSecurityStampAsync(User);
        return user is not null && await users.IsInRoleAsync(user, "Driver") ? await tasks.FindDriverAsync(user.Id, cancellationToken) : null;
    }
}
