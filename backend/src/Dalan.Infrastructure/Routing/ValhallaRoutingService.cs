using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dalan.Application.Routing;

namespace Dalan.Infrastructure.Routing;

public sealed class ValhallaRoutingService(HttpClient client) : IRoutingService
{
    public Task<RoadRoute?> GetRouteAsync(GeoPoint origin, GeoPoint destination, CancellationToken cancellationToken = default)
        => Route(origin, destination, false, cancellationToken);
    public Task<RoadRoute?> GetDirectionsAsync(GeoPoint origin, GeoPoint destination, CancellationToken cancellationToken = default)
        => Route(origin, destination, true, cancellationToken);
    private async Task<RoadRoute?> Route(GeoPoint origin, GeoPoint destination, bool directions, CancellationToken cancellationToken)
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
            directions_type = directions ? "instructions" : "none"
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
        var legs = trip.GetProperty("legs");
        var shape = legs.GetArrayLength() == 0 ? null : legs[0].GetProperty("shape").GetString();
        if (!double.IsFinite(distanceKilometers) || distanceKilometers < 0
            || !double.IsFinite(durationSeconds) || durationSeconds < 0
            || string.IsNullOrWhiteSpace(shape))
            throw new InvalidDataException("Valhalla returned an invalid route.");

        var maneuvers = new List<RouteManeuver>();
        if (directions && legs.GetArrayLength() == 1 && legs[0].TryGetProperty("maneuvers", out var instructions))
        {
            if (instructions.GetArrayLength() > 1000) throw new InvalidDataException("Too many route maneuvers.");
            foreach (var instruction in instructions.EnumerateArray())
            {
                var text = instruction.GetProperty("instruction").GetString();
                var meters = instruction.GetProperty("length").GetDouble() * 1000;
                var seconds = instruction.GetProperty("time").GetDouble();
                var begin = instruction.GetProperty("begin_shape_index").GetInt32();
                var end = instruction.GetProperty("end_shape_index").GetInt32();
                if (string.IsNullOrWhiteSpace(text) || text.Length > 1024 || !double.IsFinite(meters)
                    || meters < 0 || !double.IsFinite(seconds) || seconds < 0 || begin < 0 || end < begin)
                    throw new InvalidDataException("Invalid route maneuver.");
                maneuvers.Add(new(text, meters, seconds, begin, end));
            }
        }
        return new RoadRoute(distanceKilometers * 1000, durationSeconds, shape, directions ? maneuvers : null);
    }
}
