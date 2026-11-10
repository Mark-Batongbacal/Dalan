namespace Dalan.Domain.Dispatch;

public enum DispatchStopKind { Pickup, Dropoff }

public sealed class DispatchPlan
{
    private DispatchPlan() { } // EF Core materialization.

    public Guid Id { get; private set; }
    public DateTimeOffset ConfirmedAtUtc { get; private set; }
    public List<DispatchRoute> Routes { get; private set; } = [];

    public static DispatchPlan Create(
        IReadOnlyList<DispatchRoute> routes,
        DateTimeOffset confirmedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(routes);

        if (routes.Count is < 1 or > 8)
            throw new ArgumentException("Provide 1 to 8 vehicle routes.", nameof(routes));
        if (routes.Any(route => route is null))
            throw new ArgumentException("Routes cannot be null.", nameof(routes));
        if (routes.Select(route => route.VehicleId).Distinct().Count() != routes.Count)
            throw new ArgumentException("A vehicle can appear only once.", nameof(routes));

        var allStops = routes.SelectMany(route => route.Stops).ToArray();
        if (allStops.Length is < 2 or > 30)
            throw new ArgumentException("Provide 1 to 15 deliveries.", nameof(routes));

        var deliveryCounts = allStops.GroupBy(stop => stop.DeliveryId);
        if (deliveryCounts.Any(group =>
                group.Count() != 2 ||
                group.Count(stop => stop.Kind == DispatchStopKind.Pickup) != 1 ||
                group.Count(stop => stop.Kind == DispatchStopKind.Dropoff) != 1))
        {
            throw new ArgumentException(
                "Each delivery needs exactly one pickup and one drop-off.",
                nameof(routes));
        }

        if (confirmedAtUtc == default)
            throw new ArgumentException("Confirmation time is required.", nameof(confirmedAtUtc));

        return new DispatchPlan
        {
            Id = Guid.NewGuid(),
            ConfirmedAtUtc = confirmedAtUtc.ToUniversalTime(),
            Routes = routes.ToList()
        };
    }
}

public sealed class DispatchRoute
{
    private DispatchRoute() { } // EF Core materialization.

    public Guid Id { get; private set; }
    public Guid VehicleId { get; private set; }
    public Guid DriverId { get; private set; }
    public double StartLatitude { get; private set; }
    public double StartLongitude { get; private set; }
    public double DistanceMeters { get; private set; }
    public double DurationSeconds { get; private set; }
    public List<DispatchStop> Stops { get; private set; } = [];

    public static DispatchRoute Create(
        Guid vehicleId,
        Guid driverId,
        double startLatitude,
        double startLongitude,
        double distanceMeters,
        double durationSeconds,
        IReadOnlyList<DispatchStop> stops)
    {
        ArgumentNullException.ThrowIfNull(stops);

        if (vehicleId == Guid.Empty || driverId == Guid.Empty)
            throw new ArgumentException("A route needs a Driver and Vehicle.");
        if (!ValidCoordinates(startLatitude, startLongitude))
            throw new ArgumentException("The vehicle start is invalid.");
        if (!double.IsFinite(distanceMeters) || distanceMeters < 0 ||
            !double.IsFinite(durationSeconds) || durationSeconds < 0)
            throw new ArgumentException("Route estimates must be finite and nonnegative.");
        ValidateStops(stops);

        return new DispatchRoute
        {
            Id = Guid.NewGuid(),
            VehicleId = vehicleId,
            DriverId = driverId,
            StartLatitude = startLatitude,
            StartLongitude = startLongitude,
            DistanceMeters = distanceMeters,
            DurationSeconds = durationSeconds,
            Stops = stops.ToList()
        };
    }

    public static void ValidateStops(IReadOnlyList<DispatchStop> stops)
    {
        ArgumentNullException.ThrowIfNull(stops);
        if (stops.Count > 30)
            throw new ArgumentException("A route may contain at most 30 stops.", nameof(stops));
        if (stops.Any(stop => stop is null))
            throw new ArgumentException("Stops cannot be null.", nameof(stops));
        if (stops.Select(stop => stop.Position)
                .Order()
                .Where((position, index) => position != index)
                .Any())
            throw new ArgumentException(
                "Stop positions must start at zero and be consecutive.", nameof(stops));

        foreach (var deliveryStops in stops.GroupBy(stop => stop.DeliveryId))
        {
            var ordered = deliveryStops.OrderBy(stop => stop.Position).ToArray();
            if (ordered.Length != 2 ||
                ordered[0].Kind != DispatchStopKind.Pickup ||
                ordered[1].Kind != DispatchStopKind.Dropoff)
            {
                throw new ArgumentException(
                    "Each pickup must precede its drop-off on the same route.",
                    nameof(stops));
            }
        }

    }

    private static bool ValidCoordinates(double latitude, double longitude)
        => double.IsFinite(latitude) && latitude is >= -90 and <= 90 &&
           double.IsFinite(longitude) && longitude is >= -180 and <= 180;
}

public sealed class DispatchStop
{
    private DispatchStop() { } // EF Core materialization.

    public Guid Id { get; private set; }
    public Guid DeliveryId { get; private set; }
    public DispatchStopKind Kind { get; private set; }
    public int Position { get; private set; }
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }

    public static DispatchStop Create(
        Guid deliveryId,
        DispatchStopKind kind,
        int position,
        double latitude,
        double longitude)
    {
        if (deliveryId == Guid.Empty || !Enum.IsDefined(kind) || position < 0 ||
            !double.IsFinite(latitude) || latitude is < -90 or > 90 ||
            !double.IsFinite(longitude) || longitude is < -180 or > 180)
        {
            throw new ArgumentException("The dispatch stop is invalid.");
        }

        return new DispatchStop
        {
            Id = Guid.NewGuid(),
            DeliveryId = deliveryId,
            Kind = kind,
            Position = position,
            Latitude = latitude,
            Longitude = longitude
        };
    }
}
