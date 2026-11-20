namespace Dalan.Application.Routing;

public sealed record GeoPoint(double Latitude, double Longitude)
{
    public bool IsValid => double.IsFinite(Latitude) && double.IsFinite(Longitude)
        && Latitude is >= -90 and <= 90 && Longitude is >= -180 and <= 180;
}

public sealed record RouteManeuver(string Instruction, double DistanceMeters, double DurationSeconds,
    int BeginShapeIndex, int EndShapeIndex);
public sealed record RoadRoute(double DistanceMeters, double DurationSeconds, string EncodedPolyline6,
    IReadOnlyList<RouteManeuver>? Maneuvers = null);

public interface IRoutingService
{
    Task<RoadRoute?> GetDirectionsAsync(GeoPoint origin, GeoPoint destination, CancellationToken cancellationToken = default)
        => GetRouteAsync(origin, destination, cancellationToken);
    // Null means Valhalla could not find a road route between valid coordinates.
    Task<RoadRoute?> GetRouteAsync(
        GeoPoint origin, GeoPoint destination, CancellationToken cancellationToken = default);
}
