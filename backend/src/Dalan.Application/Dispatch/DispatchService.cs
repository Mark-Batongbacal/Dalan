using Dalan.Application.FleetOptimization;
using Dalan.Application.Routing;
using Dalan.Domain.Deliveries;
using Dalan.Domain.Dispatch;
using Dalan.Domain.Drivers;
using Dalan.Domain.Vehicles;
using Dalan.Contracts.Dispatch;

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

        if (!Enum.IsDefined(request.Strategy))
            throw new ArgumentException("Select a supported dispatch strategy.", nameof(request));

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

        var options = Options(request.Settings);
        var fleetRequest = new FleetOptimizationRequest(
            request.Vehicles.Select(vehicle => new FleetVehicle(vehicle.VehicleId, vehicle.Start,
                vehiclesById[vehicle.VehicleId].PayloadCapacityKg)).ToArray(), jobs, request.Strategy, options);
        FleetOptimizer.ValidateOptions(fleetRequest);
        DispatchRotation? rotation = null;
        if (request.Strategy == DispatchStrategy.WarehouseClusterRotation)
        {
            rotation = await repository.ReadRotationAsync(FleetOptimizer.WarehouseKey(options.Warehouse!), cancellationToken);
            var byDriver = driversByVehicle.ToDictionary(pair => pair.Value.Id, pair => pair.Key);
            options = options with { VehiclePriority = rotation.OrderRiders(byDriver.Keys).Select(id => byDriver[id]).ToArray() };
        }
        var optimized = await optimizer.OptimizeAsync(fleetRequest with { Options = options }, cancellationToken);

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
                    delivery.Id, delivery.RowVersion.ToArray())).ToArray(), request.Strategy,
            Metrics(optimized, request.Strategy, options), rotation?.Version);
    }

    public async Task<DispatchPlan?> ConfirmAsync(
    DispatchConfirmRequest request,
    CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Enum.IsDefined(request.Strategy))
            throw new ArgumentException("Select a supported dispatch strategy.", nameof(request));

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

        if (request.Routes.Sum(route => route.Stops.Count) is < 2 or > 30)
            throw new ArgumentException("Provide 1 to 15 deliveries in the reviewed routes.");

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
        var options = Options(request.Settings);
        FleetOptimizer.ValidateOptions(new FleetOptimizationRequest(
            request.Vehicles.Select(x => new FleetVehicle(x.VehicleId, x.Start, vehicles[x.VehicleId].PayloadCapacityKg)).ToArray(),
            candidates.Deliveries.Select(x => new FleetJob(x.Id.ToString("N"),
                new GeoPoint(x.PickupLatitude, x.PickupLongitude), new GeoPoint(x.DropoffLatitude, x.DropoffLongitude))).ToArray(),
            request.Strategy, options));
        DispatchRotation? rotation = null;
        Guid? firstRider = null;
        if (request.Strategy == DispatchStrategy.WarehouseClusterRotation)
        {
            if (request.RotationVersion is not >= 0)
                throw new ArgumentException("A warehouse preview rotation version is required.");
            rotation = await repository.ReadRotationAsync(FleetOptimizer.WarehouseKey(options.Warehouse!), cancellationToken);
            if (rotation.Version != request.RotationVersion) return null;
        }
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

            DispatchRoute.ValidateStops(stops);
            if (request.Strategy != DispatchStrategy.MinimumTotalTravelTime)
            {
                if (stops.Count > 0 && vehicle.PayloadCapacityKg is null) return null;
                var load = 0m;
                var departed = false;
                foreach (var stop in stops)
                {
                    if (deliveries[stop.DeliveryId].PayloadWeightKg is not decimal weight) return null;
                    if (request.Strategy == DispatchStrategy.WarehouseClusterRotation)
                    {
                        if (stop.Kind == DispatchStopKind.Pickup && departed) return null;
                        if (stop.Kind == DispatchStopKind.Dropoff) departed = true;
                    }
                    load += stop.Kind == DispatchStopKind.Pickup ? weight : -weight;
                    if (load < 0 || load > vehicle.PayloadCapacityKg) return null;
                }
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
                    if (!double.IsFinite(leg.DistanceMeters) || leg.DistanceMeters < 0
                        || !double.IsFinite(leg.DurationSeconds) || leg.DurationSeconds < 0)
                        throw new InvalidDataException("Routing matrix contains invalid estimates.");

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

        if (options.MaxFleetTravelSeconds is double budget && confirmedRoutes.Sum(x => x.DurationSeconds) > budget + 0.001)
            return null;
        if (rotation is not null) firstRider = rotation.OrderRiders(confirmedRoutes.Select(x => x.DriverId))[0];
        var plan = DispatchPlan.Create(confirmedRoutes, timeProvider.GetUtcNow(),
            new DispatchOptimizationInfo(request.Strategy.ToString(), options.ServiceSecondsPerStop, options.MaxFleetTravelSeconds,
                rotation is null ? null : FleetOptimizer.WarehouseKey(options.Warehouse!), rotation?.Version, firstRider));

        return await repository.ConfirmAsync(
            plan,
            request.DeliveryVersions.Where(version => selectedIds.Contains(version.DeliveryId)).ToArray(),
            cancellationToken)
            ? plan
            : null;
    }

    private static FleetOptimizationOptions Options(DispatchStrategySettings? settings)
        => settings is null ? new() : new(
            settings.Warehouse is null ? null : new GeoPoint(settings.Warehouse.Latitude, settings.Warehouse.Longitude),
            settings.ServiceSecondsPerStop, settings.MaxFleetTravelSeconds);

    private static DispatchPlanMetrics Metrics(FleetOptimizationResult result, DispatchStrategy strategy, FleetOptimizationOptions options)
    {
        var routes = result.Routes.Select(x => new DispatchRouteMetrics(x.VehicleId,
            x.Stops.Count / 2, x.DurationSeconds, x.DurationSeconds + x.Stops.Count * options.ServiceSecondsPerStop)).ToArray();
        var warnings = new List<string>
        {
            "Estimates exclude existing work outside this batch, shift availability, traffic changes and deadlines.",
            options.ServiceSecondsPerStop == 0 ? "Handling time is set to zero: workload estimates include driving only."
                : $"Workload assumes {options.ServiceSecondsPerStop} seconds of handling at each pickup and drop-off."
        };
        if (strategy == DispatchStrategy.MinimumTotalTravelTime)
            warnings.Add("The legacy strategy checks individual weights only; cumulative capacity is not guaranteed.");
        else
            warnings.Add("The selected strategy requires known delivery weights and vehicle capacities; unknown loads are unassigned.");
        if (strategy == DispatchStrategy.WorkloadEquity && options.MaxFleetTravelSeconds is null)
            warnings.Add("Equity minimizes workload spread before total travel; no fleet travel budget is set.");
        if (strategy == DispatchStrategy.WarehouseClusterRotation)
            warnings.Add("Clusters contain at most 3 destinations with all pairwise road times at most 10 minutes in both directions. Rotation advances only after confirmation.");
        return new(routes.Sum(x => x.TravelSeconds), routes.Max(x => x.EstimatedWorkloadSeconds),
            routes.Max(x => x.EstimatedWorkloadSeconds) - routes.Min(x => x.EstimatedWorkloadSeconds),
            options.ServiceSecondsPerStop, routes, warnings);
    }

}
