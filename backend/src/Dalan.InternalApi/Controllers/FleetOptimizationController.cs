using Dalan.Application.FleetOptimization;
using Dalan.Application.Routing;
using Dalan.Application.Vehicles;
using Dalan.Contracts.FleetOptimization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dalan.InternalApi.Controllers;

[ApiController]
[Authorize(Roles = "Dispatcher")]
[Route("api/fleet/optimize")]
public sealed class FleetOptimizationController(
    IVehicleService vehicles, IServiceProvider services) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<FleetOptimizeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<FleetOptimizeResponse>> Optimize(
        FleetOptimizeRequest request, CancellationToken cancellationToken)
    {
        if (request.Vehicles is null || request.Jobs is null
            || request.Vehicles.Any(vehicle => vehicle is null || vehicle.Start is null)
            || request.Jobs.Any(job => job is null || job.Pickup is null || job.Dropoff is null))
        {
            ModelState.AddModelError(nameof(request), "Vehicles and jobs with coordinates are required.");
            return ValidationProblem(ModelState);
        }

        foreach (var vehicle in request.Vehicles)
        {
            if (await vehicles.GetByIdAsync(vehicle.VehicleId, cancellationToken) is null)
            {
                ModelState.AddModelError(nameof(request.Vehicles),
                    $"Vehicle {vehicle.VehicleId} is not registered.");
                return ValidationProblem(ModelState);
            }
        }

        var optimizer = services.GetService<IFleetOptimizer>();
        if (optimizer is null)
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Routing is unavailable",
                detail: "Configure a Valhalla base URL before optimizing a fleet plan.");

        try
        {
            var plan = await optimizer.OptimizeAsync(new(
                request.Vehicles.Select(vehicle => new FleetVehicle(vehicle.VehicleId,
                    new GeoPoint(vehicle.Start.Latitude, vehicle.Start.Longitude))).ToArray(),
                request.Jobs.Select(job => new FleetJob(job.JobId,
                    new GeoPoint(job.Pickup.Latitude, job.Pickup.Longitude),
                    new GeoPoint(job.Dropoff.Latitude, job.Dropoff.Longitude),
                    job.AssignedVehicleId)).ToArray()), cancellationToken);

            return Ok(new FleetOptimizeResponse(
                plan.Routes.Select(route => new OptimizedVehicleRouteResponse(
                    route.VehicleId,
                    route.Stops.Select(stop => new FleetStopResponse(
                        stop.JobId, stop.Kind.ToString(),
                        new CoordinateRequest(stop.Point.Latitude, stop.Point.Longitude))).ToArray(),
                    route.DistanceMeters, route.DurationSeconds)).ToArray(),
                plan.UnassignedJobIds));
        }
        catch (ArgumentException error)
        {
            ModelState.AddModelError(nameof(request), error.Message);
            return ValidationProblem(ModelState);
        }
        catch (HttpRequestException)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "Routing provider failed");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Problem(statusCode: StatusCodes.Status504GatewayTimeout,
                title: "Routing provider timed out");
        }
        catch (InvalidDataException)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "Routing provider returned invalid data");
        }
    }
}
