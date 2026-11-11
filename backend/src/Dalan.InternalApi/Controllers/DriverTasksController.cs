using System.Text.Json;
using Dalan.Application.DriverExecution;
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
            || await tasks.FindDriverAsync(user.Id, cancellationToken) is null)
            return Unauthorized();

        var result = await signInManager.CheckPasswordSignInAsync(
            user, request.Password!, lockoutOnFailure: true);
        if (!result.Succeeded) return Unauthorized();

        return SignIn(await signInManager.CreateUserPrincipalAsync(user), Scheme);
    }

    [HttpGet("session")]
    public async Task<IActionResult> Session(CancellationToken cancellationToken) =>
        await CurrentDriver(cancellationToken) is Guid id ? Ok(new { driverId = id }) : Unauthorized();

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
            _ => BadRequest(new { message = "The event time or proof is invalid." })
        };
    }

    private async Task<Guid?> CurrentDriver(CancellationToken cancellationToken)
    {
        var user = await signInManager.ValidateSecurityStampAsync(User);
        return user is not null && await users.IsInRoleAsync(user, Role)
            ? await tasks.FindDriverAsync(user.Id, cancellationToken)
            : null;
    }
}
