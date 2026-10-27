using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dalan.Application.Routing;

namespace Dalan.Infrastructure.Routing;

public sealed class ValhallaRoutingService(HttpClient client) : IRoutingService
{
    public async Task<RoadRoute?> GetRouteAsync(
        GeoPoint origin, GeoPoint destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(destination);
        if (!origin.IsValid)
            throw new ArgumentOutOfRangeException(nameof(origin), "Route coordinates must be finite valid latitude and longitude values.");
        if (!destination.IsValid)
            throw new ArgumentOutOfRangeException(nameof(destination), "Route coordinates must be finite valid latitude and longitude values.");

        using var response = await client.PostAsJsonAsync("route", new
        {
            locations = new[]
            {
                new { lat = origin.Latitude, lon = origin.Longitude },
                new { lat = destination.Latitude, lon = destination.Longitude }
            },
            costing = "auto",
            units = "kilometers",
            directions_type = "none"
        }, cancellationToken);

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            try
            {
                using var error = await JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                if (error.RootElement.TryGetProperty("error_code", out var code)
                    && code.ValueKind == JsonValueKind.Number
                    && code.TryGetInt32(out var value) && value is 170 or 171 or 441 or 442 or 443)
                    return null;
            }
            catch (JsonException)
            {
                // Preserve the HTTP error if the provider returned an unparseable body.
            }
        }

        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var trip = body.RootElement.GetProperty("trip");
        var summary = trip.GetProperty("summary");
        var distanceKilometers = summary.GetProperty("length").GetDouble();
        var durationSeconds = summary.GetProperty("time").GetDouble();
        var shape = trip.GetProperty("legs")[0].GetProperty("shape").GetString();
        if (!double.IsFinite(distanceKilometers) || distanceKilometers < 0
            || !double.IsFinite(durationSeconds) || durationSeconds < 0
            || string.IsNullOrWhiteSpace(shape))
            throw new InvalidDataException("Valhalla returned an invalid route.");

        return new RoadRoute(distanceKilometers * 1000, durationSeconds, shape);
    }
}
