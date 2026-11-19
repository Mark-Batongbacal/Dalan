namespace Dalan.Application.Routing;

public sealed record PlaceTextAttribution(string Provider, string? Uri);
public sealed record PlaceTextResult(string Name, string Address, string? GoogleMapsUri,
    IReadOnlyList<PlaceTextAttribution> Attributions);

// Deliberately text-only: Google content never supplies coordinates to the MapLibre workflow.
public interface IGooglePlacesSearch
{
    Task<IReadOnlyList<PlaceTextResult>> SearchAsync(string query, CancellationToken cancellationToken = default);
}
