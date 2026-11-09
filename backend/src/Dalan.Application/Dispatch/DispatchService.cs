using Dalan.Application.FleetOptimization;
using Dalan.Application.Routing;
using Dalan.Domain.Deliveries;
using Dalan.Domain.Dispatch;
using Dalan.Domain.Drivers;
using Dalan.Domain.Vehicles;

namespace Dalan.Application.Dispatch;

public sealed class DispatchService(
    IDispatchRepository repository,
    IFleetOptimizer optimizer,
    ITravelMatrixService travelMatrix,
    TimeProvider timeProvider) : IDispatchService
{
    public async Task<DispatchPreviewResult> PreviewAsync(
        DispatchPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Vehicles is null ||
            request.Vehicles.Count is < 1 or > 8 ||
            request.Vehicles.Any(vehicle =>
                vehicle is null ||
                vehicle.VehicleId == Guid.Empty ||
                vehicle.Start is null ||
                !vehicle.Start.IsValid) ||
            request.Vehicles.Select(vehicle => vehicle.VehicleId)
                .Distinct().Count() != request.Vehicles.Count)
        {
            throw new ArgumentException(
                "Provide 1 to 8 unique vehicles with valid starts.",
                nameof(request));
        }

        if (request.DeliveryIds is null ||
            request.DeliveryIds.Count is < 1 or > 15 ||
            request.DeliveryIds.Any(id => id == Guid.Empty) ||
            request.DeliveryIds.Distinct().Count() != request.DeliveryIds.Count)
        {
            throw new ArgumentException(
                "Provide 1 to 15 unique delivery IDs.",
                nameof(request));
        }

        var candidates = await repository.ReadCandidatesAsync(
            request.DeliveryIds,
            request.Vehicles.Select(vehicle => vehicle.VehicleId).ToArray(),
            cancellationToken);

        if (candidates.Deliveries.Count != request.DeliveryIds.Count ||
            candidates.Vehicles.Count != request.Vehicles.Count)
            throw new KeyNotFoundException("A selected delivery or vehicle was not found.");

        var vehiclesById = candidates.Vehicles.ToDictionary(vehicle => vehicle.Id);

        if (candidates.AlreadyDispatchedDeliveryIds.Count > 0 ||
            candidates.Deliveries.Any(delivery =>
                delivery.Status is not (DeliveryStatus.Pending or DeliveryStatus.Assigned)))
        {
            throw new InvalidOperationException(
                "A selected delivery is no longer available for dispatch.");
        }

        var today = DateOnly.FromDateTime(
            timeProvider.GetUtcNow().UtcDateTime);

        var driversByVehicle = new Dictionary<Guid, Driver>();
        foreach (var vehicle in candidates.Vehicles)
        {
            if (vehicle.Availability != VehicleAvailability.Available)
                throw new InvalidOperationException(
                    "Only available vehicles can be planned.");

            var driver = candidates.Drivers.SingleOrDefault(candidate =>
                candidate.AssignedVehicleId == vehicle.Id &&
                candidate.Availability == DriverAvailability.Available &&
                candidate.LicenseExpiry >= today);

            if (driver is null)
                throw new InvalidOperationException(
                    "Each vehicle needs an available assigned Driver with a current license.");

            driversByVehicle.Add(vehicle.Id, driver);
        }

        var jobs = candidates.Deliveries.Select(delivery =>
        {
            if (delivery.Status == DeliveryStatus.Assigned &&
                (!driversByVehicle.TryGetValue(
                    delivery.VehicleId!.Value, out var assignedDriver) ||
                 assignedDriver.Id != delivery.DriverId))
            {
                throw new InvalidOperationException(
                    "An existing delivery assignment is not eligible for this plan.");
            }

            return new FleetJob(
                delivery.Id.ToString("N"),
                new GeoPoint(delivery.PickupLatitude, delivery.PickupLongitude),
                new GeoPoint(delivery.DropoffLatitude, delivery.DropoffLongitude),
                delivery.Status == DeliveryStatus.Assigned ? delivery.VehicleId: null, delivery.PayloadWeightKg); 
        }).ToArray();

        var optimized = await optimizer.OptimizeAsync(
            new FleetOptimizationRequest(
                request.Vehicles.Select(vehicle =>
                    new FleetVehicle( vehicle.VehicleId, vehicle.Start, vehiclesById[vehicle.VehicleId].PayloadCapacityKg)
                ).ToArray(), 
                jobs),
            cancellationToken);

        var startsByVehicle = request.Vehicles.ToDictionary(
            vehicle => vehicle.VehicleId,
            vehicle => vehicle.Start);

        return new DispatchPreviewResult(
            optimized.Routes.Select(route => new DispatchPreviewRoute(
                route.VehicleId,
                driversByVehicle[route.VehicleId].Id,
                startsByVehicle[route.VehicleId],
                route.Stops.Select(stop => new DispatchPreviewStop(
                    Guid.Parse(stop.JobId),
                    stop.Kind == FleetStopKind.Pickup
                        ? Dalan.Domain.Dispatch.DispatchStopKind.Pickup
                        : Dalan.Domain.Dispatch.DispatchStopKind.Dropoff,
                    stop.Point)).ToArray(),
                route.DistanceMeters,
                route.DurationSeconds)).ToArray(),
            optimized.UnassignedJobIds.Select(Guid.Parse).ToArray(),
            candidates.Deliveries.Select(delivery =>
                new DispatchDeliveryVersion(
                    delivery.Id, delivery.RowVersion.ToArray())).ToArray());
    }

    public async Task<DispatchPlan?> ConfirmAsync(
    DispatchConfirmRequest request,
    CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Vehicles is null ||
            request.Routes is null ||
            request.DeliveryVersions is null ||
            request.Vehicles.Count is < 1 or > 8 ||
            request.Routes.Count != request.Vehicles.Count ||
            request.DeliveryVersions.Count is < 1 or > 15 ||
            request.Vehicles.Any(vehicle =>
                vehicle is null ||
                vehicle.VehicleId == Guid.Empty ||
                vehicle.Start is null ||
                !vehicle.Start.IsValid) ||
            request.Routes.Any(route =>
                route is null ||
                route.VehicleId == Guid.Empty ||
                route.Stops is null) ||
            request.DeliveryVersions.Any(version =>
                version is null ||
                version.DeliveryId == Guid.Empty ||
                version.RowVersion is null ||
                version.RowVersion.Length == 0))
        {
            throw new ArgumentException("The reviewed dispatch plan is invalid.");
        }

        if (request.Vehicles.Select(vehicle => vehicle.VehicleId).Distinct().Count()
                != request.Vehicles.Count ||
            request.Routes.Select(route => route.VehicleId).Distinct().Count()
                != request.Routes.Count ||
            request.DeliveryVersions.Select(version => version.DeliveryId)
                .Distinct().Count() != request.DeliveryVersions.Count ||
            request.Routes.Any(route =>
                route.Stops.Any(stop => stop is null || stop.DeliveryId == Guid.Empty)))
        {
            throw new ArgumentException(
                "Vehicle, delivery version, and stop IDs must be valid and unique where required.");
        }

        var starts = request.Vehicles.ToDictionary(
            vehicle => vehicle.VehicleId,
            vehicle => vehicle.Start);

        if (request.Routes.Any(route => !starts.ContainsKey(route.VehicleId)))
        {
            throw new ArgumentException(
                "Each reviewed route needs one unique vehicle start.");
        }

        var versions = request.DeliveryVersions.ToDictionary(
            version => version.DeliveryId,
            version => version.RowVersion);

        var selectedIds = request.Routes
            .SelectMany(route => route.Stops)
            .Select(stop => stop.DeliveryId)
            .Distinct()
            .ToArray();

        if (selectedIds.Length == 0 ||
            selectedIds.Any(id => !versions.ContainsKey(id)))
        {
            throw new ArgumentException(
                "The reviewed stops must match the previewed deliveries.");
        }

        var candidates = await repository.ReadCandidatesAsync(
            selectedIds,
            starts.Keys.ToArray(),
            cancellationToken);

        if (candidates.Deliveries.Count != selectedIds.Length ||
            candidates.Vehicles.Count != starts.Count)
            throw new KeyNotFoundException(
                "A selected delivery or vehicle was not found.");

        if (candidates.AlreadyDispatchedDeliveryIds.Count > 0 ||
            candidates.Deliveries.Any(delivery =>
                delivery.Status is not (DeliveryStatus.Pending or DeliveryStatus.Assigned) ||
                !delivery.RowVersion.SequenceEqual(versions[delivery.Id])))
        {
            return null;
        }

        var deliveries = candidates.Deliveries.ToDictionary(delivery => delivery.Id);
        var vehicles = candidates.Vehicles.ToDictionary(vehicle => vehicle.Id);
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var confirmedRoutes = new List<DispatchRoute>();

        foreach (var reviewedRoute in request.Routes)
        {
            var vehicle = vehicles[reviewedRoute.VehicleId];
            var driver = candidates.Drivers.SingleOrDefault(candidate =>
                candidate.AssignedVehicleId == vehicle.Id &&
                candidate.Availability == DriverAvailability.Available &&
                candidate.LicenseExpiry >= today);

            if (vehicle.Availability != VehicleAvailability.Available ||
                driver is null)
            {
                return null;
            }

            var stops = new List<DispatchStop>();
            for (var position = 0; position < reviewedRoute.Stops.Count; position++)
            {
                var choice = reviewedRoute.Stops[position];

                if (!deliveries.TryGetValue(choice.DeliveryId, out var delivery))
                    throw new ArgumentException("A stop names an unknown delivery.");

                if (delivery.Status == DeliveryStatus.Assigned &&
                    (delivery.VehicleId != vehicle.Id ||
                     delivery.DriverId != driver.Id))
                {
                    return null;
                }

                if (delivery.PayloadWeightKg is decimal weight &&
                    vehicle.PayloadCapacityKg is decimal capacity &&
                    weight > capacity)
                {
                    return null;
                }

                var latitude = choice.Kind == DispatchStopKind.Pickup
                    ? delivery.PickupLatitude
                    : delivery.DropoffLatitude;
                var longitude = choice.Kind == DispatchStopKind.Pickup
                    ? delivery.PickupLongitude
                    : delivery.DropoffLongitude;

                stops.Add(DispatchStop.Create(
                    delivery.Id, choice.Kind, position, latitude, longitude));
            }

            var points = new List<GeoPoint>
        {
            starts[vehicle.Id]
        };
            points.AddRange(stops.Select(stop =>
                new GeoPoint(stop.Latitude, stop.Longitude)));

            var distance = 0d;
            var duration = 0d;
            if (stops.Count > 0)
            {
                var matrix = await travelMatrix.GetMatrixAsync(
                    points, cancellationToken);

                if (matrix.GetLength(0) != points.Count ||
                    matrix.GetLength(1) != points.Count)
                    throw new InvalidDataException(
                        "Routing matrix dimensions are invalid.");

                for (var index = 0; index < points.Count - 1; index++)
                {
                    var leg = matrix[index, index + 1];
                    if (leg is null)
                        throw new InvalidOperationException(
                            "The reviewed route has an unreachable leg.");

                    distance += leg.DistanceMeters;
                    duration += leg.DurationSeconds;
                }
            }

            confirmedRoutes.Add(DispatchRoute.Create(
                vehicle.Id,
                driver.Id,
                points[0].Latitude,
                points[0].Longitude,
                distance,
                duration,
                stops));
        }

        var plan = DispatchPlan.Create(
            confirmedRoutes, timeProvider.GetUtcNow());

        return await repository.ConfirmAsync(
            plan,
            request.DeliveryVersions.Where(version => selectedIds.Contains(version.DeliveryId)).ToArray(),
            cancellationToken)
            ? plan
            : null;
    }


}
