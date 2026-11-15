using System.Text.Json;
using Dalan.Application.Routing;

namespace Dalan.Infrastructure.Routing;

public sealed class PhotonPlaceSearchService(HttpClient client) : IPlaceSearchService
{
    public async Task<IReadOnlyList<PlaceSearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        query = query.Trim();
        if (query.Length is < 3 or > 200) throw new ArgumentException("Enter 3–200 characters.", nameof(query));
        using var response = await client.GetAsync("api/?q=" + Uri.EscapeDataString(query) + "&limit=5",
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(1_048_576, cancellationToken);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        if (json.RootElement.ValueKind != JsonValueKind.Object ||
            !json.RootElement.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The geocoder returned invalid data.");
        var results = new List<PlaceSearchResult>();
        foreach (var feature in features.EnumerateArray())
        {
            if (results.Count == 5) break;
            if (feature.ValueKind != JsonValueKind.Object || !feature.TryGetProperty("geometry", out var geometry) ||
                geometry.ValueKind != JsonValueKind.Object ||
                !geometry.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "Point" ||
                !geometry.TryGetProperty("coordinates", out var coords) || coords.ValueKind != JsonValueKind.Array ||
                coords.GetArrayLength() != 2 || coords[0].ValueKind != JsonValueKind.Number || coords[1].ValueKind != JsonValueKind.Number ||
                !coords[0].TryGetDouble(out var lon) || !coords[1].TryGetDouble(out var lat) ||
                !feature.TryGetProperty("properties", out var props) || props.ValueKind != JsonValueKind.Object) continue;
            var point = new GeoPoint(lat, lon);
            if (!point.IsValid) continue;
            var parts = new[] { "name", "housenumber", "street", "city", "state", "country" }
                .Select(key => props.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null)
                .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal);
            var label = string.Join(", ", parts);
            if (label.Length is 0 or > 500) continue;
            results.Add(new(label, point));
        }
        return results;
    }
}
