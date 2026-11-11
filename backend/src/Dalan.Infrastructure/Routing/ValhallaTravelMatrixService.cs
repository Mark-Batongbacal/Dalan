using System.Net.Http.Json;
using System.Text.Json;
using Dalan.Application.Routing;

namespace Dalan.Infrastructure.Routing;

public sealed record ValhallaMatrixLimits(int MaxMatrixCells = 100);

public sealed class ValhallaTravelMatrixService(HttpClient client, ValhallaMatrixLimits? limits = null) : ITravelMatrixService
{
    public async Task<TravelMetric?[,]> GetMatrixAsync(
        IReadOnlyList<GeoPoint> points, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0 || points.Any(point => point is null || !point.IsValid))
            throw new ArgumentException("At least one valid coordinate is required.", nameof(points));

        var maxCells = limits?.MaxMatrixCells ?? 100;
        if (maxCells is < 1 or > 10000) throw new ArgumentException("MaxMatrixCells must be 1–10000.");
        var unique = points.Distinct().ToArray();
        var indices = unique.Select((point, index) => (point, index)).ToDictionary(x => x.point, x => x.index);
        var lookup = points.Select(point => indices[point]).ToArray();
        var locations = unique.Select(point => new { lat = point.Latitude, lon = point.Longitude }).ToArray();
        var combined = new TravelMetric?[unique.Length, unique.Length];
        var blockSize = (int)Math.Sqrt(maxCells);

        try
        {
            // One logical matrix, with bounded sequential HTTP blocks for provider limits.
            for (var source = 0; source < unique.Length; source += blockSize)
            for (var target = 0; target < unique.Length; target += blockSize)
            {
                var sources = locations.Skip(source).Take(blockSize).ToArray();
                var targets = locations.Skip(target).Take(blockSize).ToArray();
                using var response = await client.PostAsJsonAsync("sources_to_targets", new
                {
                    sources, targets, costing = "auto", units = "kilometers", verbose = false
                }, cancellationToken);
                response.EnsureSuccessStatusCode();
                using var body = await JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                var matrix = body.RootElement.GetProperty("sources_to_targets");
                var durations = matrix.GetProperty("durations");
                var distances = matrix.GetProperty("distances");
                if (durations.GetArrayLength() != sources.Length || distances.GetArrayLength() != sources.Length)
                    throw new InvalidDataException("Valhalla returned an invalid matrix size.");

                for (var from = 0; from < sources.Length; from++)
                {
                    var timeRow = durations[from];
                    var distanceRow = distances[from];
                    if (timeRow.GetArrayLength() != targets.Length || distanceRow.GetArrayLength() != targets.Length)
                        throw new InvalidDataException("Valhalla returned an invalid matrix row.");

                    for (var to = 0; to < targets.Length; to++)
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
                        combined[source + from, target + to] = new TravelMetric(kilometers * 1000, seconds);
                    }
                }
            }
            var result = new TravelMetric?[points.Count, points.Count];
            for (var from = 0; from < points.Count; from++)
            for (var to = 0; to < points.Count; to++) result[from, to] = combined[lookup[from], lookup[to]];
            return result;
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidDataException("Valhalla returned an invalid matrix response.", error);
        }
    }
}
