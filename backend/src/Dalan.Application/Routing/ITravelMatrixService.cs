namespace Dalan.Application.Routing;

public sealed record TravelMetric(double DistanceMeters, double DurationSeconds);

public interface ITravelMatrixService
{
    Task<TravelMetric?[,]> GetMatrixAsync(
        IReadOnlyList<GeoPoint> points, CancellationToken cancellationToken = default);
}
