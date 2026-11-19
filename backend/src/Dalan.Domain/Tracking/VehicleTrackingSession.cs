namespace Dalan.Domain.Tracking;

public sealed class VehicleTrackingSession
{
    private VehicleTrackingSession() { }
    public Guid Id { get; private set; }
    public Guid DriverId { get; private set; }
    public Guid VehicleId { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? EndedAtUtc { get; private set; }
    public DateTimeOffset? LastCapturedAtUtc { get; private set; }
    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }
    public double? AccuracyMeters { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static VehicleTrackingSession Start(Guid driverId, Guid vehicleId, DateTimeOffset now)
    {
        if (driverId == Guid.Empty || vehicleId == Guid.Empty || now == default) throw new ArgumentException("A current Driver, Vehicle and timestamp are required.");
        return new() { Id = Guid.NewGuid(), DriverId = driverId, VehicleId = vehicleId, StartedAtUtc = now.ToUniversalTime() };
    }

    public void Stop(DateTimeOffset now) => EndedAtUtc ??= now.ToUniversalTime();

    public VehicleTrackingSample Observe(Guid sampleId, double latitude, double longitude, double accuracy,
        DateTimeOffset capturedAt, DateTimeOffset now, string hash)
    {
        var captured = capturedAt.ToUniversalTime();
        if (EndedAtUtc is not null) throw new InvalidOperationException("Tracking has stopped.");
        if (sampleId == Guid.Empty || !double.IsFinite(latitude) || latitude is < -90 or > 90
            || !double.IsFinite(longitude) || longitude is < -180 or > 180 || !double.IsFinite(accuracy) || accuracy is < 0 or > 100
            || capturedAt == default || captured > now.AddSeconds(15) || captured < now.AddMinutes(-2)
            || captured < StartedAtUtc.AddSeconds(-15)) throw new ArgumentException("A fresh, accurate GPS sample is required.");
        if (LastCapturedAtUtc is DateTimeOffset previous && captured <= previous) throw new InvalidOperationException("GPS samples must arrive in timestamp order.");
        var elapsed = LastCapturedAtUtc is DateTimeOffset last ? (captured - last).TotalSeconds : 0;
        var observed = elapsed is > 0 and <= 120 ? elapsed : 0;
        var distance = observed > 0 ? Distance(Latitude!.Value, Longitude!.Value, latitude, longitude) : 0;
        if (observed > 0 && distance / observed > 55) throw new ArgumentException("The GPS jump is not plausible for road tracking.");
        var moving = observed > 0 && distance > Math.Max(accuracy, AccuracyMeters ?? 0) && distance / observed >= 0.5;
        var sample = new VehicleTrackingSample
        {
            SessionId = Id, SampleId = sampleId, VehicleId = VehicleId, DriverId = DriverId, CapturedAtUtc = captured,
            IntervalStartUtc = observed > 0 ? LastCapturedAtUtc!.Value : captured, Latitude = latitude, Longitude = longitude,
            AccuracyMeters = accuracy, ObservedSeconds = observed, Moving = moving, DistanceMeters = moving ? distance : 0, PayloadHash = hash
        };
        LastCapturedAtUtc = captured; Latitude = latitude; Longitude = longitude; AccuracyMeters = accuracy;
        return sample;
    }

    private static double Distance(double latitude, double longitude, double nextLatitude, double nextLongitude)
    {
        var radians = Math.PI / 180;
        var dLat = (nextLatitude - latitude) * radians;
        var dLon = (nextLongitude - longitude) * radians;
        var a = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(latitude * radians) * Math.Cos(nextLatitude * radians) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 6_371_000 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1)));
    }
}

public sealed class VehicleTrackingSample
{
    public Guid SessionId { get; init; }
    public Guid SampleId { get; init; }
    public Guid DriverId { get; init; }
    public Guid VehicleId { get; init; }
    public DateTimeOffset CapturedAtUtc { get; init; }
    public DateTimeOffset IntervalStartUtc { get; init; }
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double AccuracyMeters { get; init; }
    public double ObservedSeconds { get; init; }
    public bool Moving { get; init; }
    public double DistanceMeters { get; init; }
    public required string PayloadHash { get; init; }
}
