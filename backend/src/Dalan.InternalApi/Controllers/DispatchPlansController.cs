using Dalan.Application.Dispatch;
using Dalan.Application.Routing;
using Dalan.Contracts.Dispatch;
using Dalan.Contracts.FleetOptimization;
using Dalan.Domain.Dispatch;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dalan.InternalApi.Controllers;

[ApiController]
[Authorize(Roles = "Dispatcher")]
[Route("api/dispatch-plans")]
public sealed class DispatchPlansController(
    IServiceProvider services, 
    IDispatchPlanReader planReader)
    : ControllerBase
{
    [HttpPost("preview")]
    public async Task<IActionResult> Preview(
        PreviewDispatchRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Vehicles is null ||
            request.DeliveryIds is null ||
            request.Vehicles.Any(vehicle =>
                vehicle is null || vehicle.Start is null))
            return Invalid("Vehicles, starts, and delivery IDs are required.");

        var dispatch = services.GetService<IDispatchService>();
        if (dispatch is null)
            return RoutingUnavailable();

        try
        {
            var result = await dispatch.PreviewAsync(
                new DispatchPreviewRequest(
                    request.Vehicles.Select(vehicle =>
                        new DispatchVehicleStart(
                            vehicle.VehicleId,
                            new GeoPoint(
                                vehicle.Start!.Latitude,
                                vehicle.Start.Longitude))).ToArray(),
                    request.DeliveryIds),
                cancellationToken);

            return Ok(new DispatchPreviewResponse(
                result.Routes.Select(route => new DispatchRouteResponse(
                    route.VehicleId,
                    route.DriverId,
                    Point(route.Start),
                    route.Stops.Select((stop, position) =>
                        new DispatchStopResponse(
                            stop.DeliveryId,
                            stop.Kind.ToString(),
                            position,
                            Point(stop.Location))).ToArray(),
                    route.DistanceMeters,
                    route.DurationSeconds)).ToArray(),
                result.UnassignedDeliveryIds,
                result.DeliveryVersions.Select(version =>
                    new DispatchDeliveryVersionRequest(
                        version.DeliveryId,
                        version.RowVersion)).ToArray()));
        }
        catch (Exception error) when (IsExpected(error, cancellationToken))
        {
            return Failure(error);
        }
    }

    [HttpPost]
    public async Task<IActionResult> Confirm(
        ConfirmDispatchRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Vehicles is null ||
            request.Routes is null ||
            request.DeliveryVersions is null ||
            request.Vehicles.Any(vehicle =>
                vehicle is null || vehicle.Start is null) ||
            request.Routes.Any(route =>
                route is null ||
                route.Stops is null ||
                route.Stops.Any(stop => stop is null)) ||
            request.DeliveryVersions.Any(version =>
                version is null || version.RowVersion is null))
        {
            return Invalid("Vehicles, routes, stops, and delivery versions are required.");
        }

        if (request.Routes.Sum(route => route.Stops!.Count) is < 2 or > 30)
            return Invalid("Provide 1 to 15 deliveries in the reviewed routes.");

        var reviewedRoutes = new List<DispatchReviewedRoute>();
        foreach (var route in request.Routes)
        {
            var stops = new List<DispatchReviewedStop>();
            foreach (var stop in route.Stops!)
            {
                if (stop.Kind is null ||
                    !Enum.TryParse<DispatchStopKind>(stop.Kind, true, out var kind) ||
                    !string.Equals(stop.Kind, kind.ToString(), StringComparison.OrdinalIgnoreCase))
                    return Invalid("Stop kind must be Pickup or Dropoff.");

                stops.Add(new DispatchReviewedStop(stop.DeliveryId, kind));
            }

            reviewedRoutes.Add(
                new DispatchReviewedRoute(route.VehicleId, stops));
        }

        var dispatch = services.GetService<IDispatchService>();
        if (dispatch is null)
            return RoutingUnavailable();

        try
        {
            var plan = await dispatch.ConfirmAsync(
                new DispatchConfirmRequest(
                    request.Vehicles.Select(vehicle =>
                        new DispatchVehicleStart(
                            vehicle.VehicleId,
                            new GeoPoint(
                                vehicle.Start!.Latitude,
                                vehicle.Start.Longitude))).ToArray(),
                    reviewedRoutes,
                    request.DeliveryVersions.Select(version =>
                        new DispatchDeliveryVersion(
                            version.DeliveryId,
                            version.RowVersion!)).ToArray()),
                cancellationToken);

            if (plan is null)
                return ConflictProblem(
                    "The preview is stale or a delivery has already been dispatched.");

            return CreatedAtAction(
                nameof(GetById),
                new { id = plan.Id },
                ToResponse(plan));
        }
        catch (Exception error) when (IsExpected(error, cancellationToken))
        {
            return Failure(error);
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
     Guid id,
     CancellationToken cancellationToken)
    {
        var plan = await planReader.GetByIdAsync(id, cancellationToken);
        return plan is null ? NotFound() : Ok(ToResponse(plan));
    }
    private IActionResult Invalid(string message)
    {
        ModelState.AddModelError("Dispatch", message);
        return ValidationProblem(ModelState);
    }

    private IActionResult ConflictProblem(string message)
        => Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Dispatch cannot be confirmed",
            detail: message);

    private IActionResult RoutingUnavailable()
        => Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Routing is unavailable");

    private static bool IsExpected(
        Exception error,
        CancellationToken cancellationToken)
        => error is ArgumentException or
                    KeyNotFoundException or
                    InvalidOperationException or
                    HttpRequestException or
                    InvalidDataException ||
           error is TaskCanceledException &&
           !cancellationToken.IsCancellationRequested;

    private IActionResult Failure(Exception error)
        => error switch
        {
            ArgumentException => Invalid(error.Message),
            KeyNotFoundException => NotFound(),
            InvalidOperationException => ConflictProblem(error.Message),
            HttpRequestException => Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Routing provider failed"),
            InvalidDataException => Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Routing provider returned invalid data"),
            TaskCanceledException => Problem(
                statusCode: StatusCodes.Status504GatewayTimeout,
                title: "Routing provider timed out"),
            _ => throw error
        };

    private static CoordinateRequest Point(GeoPoint point)
        => new(point.Latitude, point.Longitude);

    private static DispatchPlanResponse ToResponse(DispatchPlan plan)
        => new(
            plan.Id,
            plan.ConfirmedAtUtc,
            plan.Routes.Select(route => new DispatchRouteResponse(
                route.VehicleId,
                route.DriverId,
                new CoordinateRequest(
                    route.StartLatitude,
                    route.StartLongitude),
                route.Stops.OrderBy(stop => stop.Position)
                    .Select(stop => new DispatchStopResponse(
                        stop.DeliveryId,
                        stop.Kind.ToString(),
                        stop.Position,
                        new CoordinateRequest(
                            stop.Latitude,
                            stop.Longitude))).ToArray(),
                route.DistanceMeters,
                route.DurationSeconds)).ToArray());
}
