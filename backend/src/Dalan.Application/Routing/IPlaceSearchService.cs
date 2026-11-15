namespace Dalan.Application.Routing;

public sealed record PlaceSearchResult(string Label, GeoPoint Location);

public interface IPlaceSearchService
{
    Task<IReadOnlyList<PlaceSearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default);
}
