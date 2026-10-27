using System.Net.Http.Json;
using System.Text.Json;
using Dalan.Application.Routing;

namespace Dalan.Infrastructure.Routing;

public sealed class ValhallaTravelMatrixService(HttpClient client) : ITravelMatrixService
{
    public async Task<TravelMetric?[,]> GetMatrixAsync(
        IReadOnlyList<GeoPoint> points, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0 || points.Any(point => point is null || !point.IsValid))
            throw new ArgumentException("At least one valid coordinate is required.", nameof(points));

        var locations = points.Select(point => new { lat = point.Latitude, lon = point.Longitude }).ToArray();
        using var response = await client.PostAsJsonAsync("sources_to_targets", new
        {
            sources = locations,
            targets = locations,
            costing = "auto",
            units = "kilometers",
            verbose = false
        }, cancellationToken);
        response.EnsureSuccessStatusCode();

        try
        {
            using var body = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var matrix = body.RootElement.GetProperty("sources_to_targets");
            var durations = matrix.GetProperty("durations");
            var distances = matrix.GetProperty("distances");
            if (durations.GetArrayLength() != points.Count || distances.GetArrayLength() != points.Count)
                throw new InvalidDataException("Valhalla returned an invalid matrix size.");

            var result = new TravelMetric?[points.Count, points.Count];
            for (var from = 0; from < points.Count; from++)
            {
                var timeRow = durations[from];
                var distanceRow = distances[from];
                if (timeRow.GetArrayLength() != points.Count || distanceRow.GetArrayLength() != points.Count)
                    throw new InvalidDataException("Valhalla returned an invalid matrix row.");

                for (var to = 0; to < points.Count; to++)
                {
                    var time = timeRow[to];
                    var distance = distanceRow[to];
                    if (time.ValueKind == JsonValueKind.Null || distance.ValueKind == JsonValueKind.Null)
                        continue;

                    var seconds = time.GetDouble();
                    var kilometers = distance.GetDouble();
                    if (!double.IsFinite(seconds) || seconds < 0
                        || !double.IsFinite(kilometers) || kilometers < 0)
                        throw new InvalidDataException("Valhalla returned an invalid travel metric.");
                    result[from, to] = new TravelMetric(kilometers * 1000, seconds);
                }
            }
            return result;
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidDataException("Valhalla returned an invalid matrix response.", error);
        }
    }
}
