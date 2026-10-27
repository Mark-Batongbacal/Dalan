namespace Dalan.Application.Routing;

public sealed record GeoPoint(double Latitude, double Longitude)
{
    public bool IsValid => double.IsFinite(Latitude) && double.IsFinite(Longitude)
        && Latitude is >= -90 and <= 90 && Longitude is >= -180 and <= 180;
}

public sealed record RoadRoute(double DistanceMeters, double DurationSeconds, string EncodedPolyline6);

public interface IRoutingService
{
    // Null means Valhalla could not find a road route between valid coordinates.
    Task<RoadRoute?> GetRouteAsync(
        GeoPoint origin, GeoPoint destination, CancellationToken cancellationToken = default);
}
