using System.Text.Json;
using Dalan.Application.DriverExecution;
using Dalan.Infrastructure.Identity;
using Dalan.Application.Routing;
using Dalan.Contracts.DriverExecution;
using Dalan.Contracts.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Dalan.InternalApi.Controllers;

[ApiController]
[Route("api/driver")]
[Authorize(AuthenticationSchemes = Scheme, Roles = Role)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DriverTasksController(
    UserManager<IdentityUser> users,
    SignInManager<IdentityUser> signInManager,
    IDriverTasks tasks) : ControllerBase
{
    private const string Scheme = "DriverBearer";
    private const string Role = "Driver";

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(StaffLoginRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(request.Email!);
        if (user is null || !await users.IsInRoleAsync(user, Role)
            || DriverMobileAccess.IsSuspended(user)
            || await tasks.FindDriverAsync(user.Id, cancellationToken) is null)
            return Unauthorized();

        var result = await signInManager.CheckPasswordSignInAsync(
            user, request.Password!, lockoutOnFailure: true);
        if (!result.Succeeded) return Unauthorized();

        return SignIn(await signInManager.CreateUserPrincipalAsync(user), Scheme);
    }

    [HttpGet("session")]
    public async Task<IActionResult> Session([FromServices] IConfiguration configuration, CancellationToken cancellationToken) =>
        await CurrentDriver(cancellationToken) is Guid id ? Ok(new { driverId = id, gpsProofEnabled = bool.TryParse(configuration["DriverGpsProof:Enabled"], out var enabled) && enabled }) : Unauthorized();

    [HttpGet("tasks")]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        await CurrentDriver(cancellationToken) is Guid id
            ? Ok(await tasks.ListAsync(id, cancellationToken))
            : Unauthorized();

    [HttpGet("tasks/{id:guid}/route")]
    public async Task<IActionResult> GetRoute(Guid id, [FromServices] IEnumerable<IRoutingService> routing,
        [FromServices] ILogger<DriverTasksController> logger, CancellationToken cancellationToken)
    {
        if (await CurrentDriver(cancellationToken) is not Guid driverId) return Unauthorized();
        var stops = await tasks.GetRouteStopsAsync(driverId, id, cancellationToken);
        if (stops is null) return NotFound();
        var service = routing.SingleOrDefault();
        if (service is null) return StatusCode(StatusCodes.Status503ServiceUnavailable);

        try
        {
            var route = await service.GetRouteAsync(stops.Pickup, stops.Dropoff, cancellationToken);
            return route is null ? UnprocessableEntity() : Ok(route);
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested &&
            error is HttpRequestException or OperationCanceledException or JsonException
                or InvalidDataException or KeyNotFoundException or InvalidOperationException)
        {
            logger.LogWarning(error, "Valhalla could not return a route for delivery {DeliveryId}", id);
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    [HttpPost("tasks/{id:guid}/events")]
    [RequestSizeLimit(700_000)]
    public async Task<IActionResult> Apply(Guid id, DriverTaskEvent request, CancellationToken cancellationToken)
    {
        if (await CurrentDriver(cancellationToken) is not Guid driverId) return Unauthorized();
        return await tasks.ApplyAsync(driverId, id, request, cancellationToken) switch
        {
            DriverEventResult.Applied => NoContent(),
            DriverEventResult.Missing => NotFound(),
            DriverEventResult.Conflict => Conflict(new { message = "The assignment or delivery status changed." }),
            DriverEventResult.GpsRejected => UnprocessableEntity(new { message = "Delivery GPS is too far from the destination, stale, or inaccurate. Recapture location near the destination or contact the dispatcher." }),
            _ => BadRequest(new { message = "The event time or proof is invalid." })
        };
    }

    [HttpPost("tasks/{id:guid}/gps-exceptions")]
    [RequestSizeLimit(704_096)]
    public async Task<IActionResult> RequestGpsReview(Guid id, GpsExceptionRequest request,
        [FromServices] IGpsExceptionService reviews, CancellationToken cancellationToken)
    {
        if (await CurrentDriver(cancellationToken) is not Guid driverId) return Unauthorized();
        var result = await reviews.Submit(driverId, id, request, cancellationToken);
        return StatusCode(result.StatusCode);
    }

    [HttpPost("navigation")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("driver-navigation")]
    [RequestSizeLimit(4096)]
    public async Task<IActionResult> Navigate(DriverNavigationRequest request,
        [FromServices] IDriverNavigationService navigation, CancellationToken cancellationToken)
    {
        if (await CurrentDriver(cancellationToken) is not Guid driverId) return Unauthorized();
        try
        {
            var result = await navigation.Navigate(driverId, request, cancellationToken);
            return result is null ? NoContent() : Ok(result);
        }
        catch (ArgumentException) { return UnprocessableEntity(new { message = "A fresh accurate location and a reachable assigned stop are required." }); }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested && error is HttpRequestException or OperationCanceledException or JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException)
        { return StatusCode(503); }
    }

    private async Task<Guid?> CurrentDriver(CancellationToken cancellationToken)
    {
        var user = await signInManager.ValidateSecurityStampAsync(User);
        return user is not null && await users.IsInRoleAsync(user, Role)
            && !await users.IsLockedOutAsync(user)
            ? await tasks.FindDriverAsync(user.Id, cancellationToken)
            : null;
    }
}
