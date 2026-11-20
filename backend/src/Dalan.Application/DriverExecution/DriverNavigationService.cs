using Dalan.Application.Routing;
namespace Dalan.Application.DriverExecution;
public sealed record DriverNavigationRequest(GeoPoint Origin, DateTimeOffset CapturedAtUtc, double AccuracyMeters, bool IncludeRemainingStops = false);
public sealed record DriverNavigationStop(Guid DeliveryId, string StopKind, string Address, GeoPoint Destination, RoadRoute Route);
public sealed record DriverNavigationRoute(Guid DeliveryId, string StopKind, string Address, GeoPoint Destination, RoadRoute Route,
    IReadOnlyList<DriverNavigationStop>? Stops = null);
public interface IDriverNavigationService
{
    Task<DriverNavigationRoute?> Navigate(Guid driverId, DriverNavigationRequest request, CancellationToken token);
}
public sealed class DriverNavigationService(IDriverTasks tasks, IEnumerable<IRoutingService> services, TimeProvider clock) : IDriverNavigationService
{
    public async Task<DriverNavigationRoute?> Navigate(Guid driverId, DriverNavigationRequest request, CancellationToken token)
    {
        if (request.Origin is null || !request.Origin.IsValid || !double.IsFinite(request.AccuracyMeters)
            || request.AccuracyMeters is < 0 or > 50 || request.CapturedAtUtc < clock.GetUtcNow().AddMinutes(-2)
            || request.CapturedAtUtc > clock.GetUtcNow().AddSeconds(5))
            throw new ArgumentException("A recent accurate position is required.");
        var assigned = await tasks.ListAsync(driverId, token);
        if (request.IncludeRemainingStops)
        {
            var stops = assigned.Where(x => x.Status is "Assigned" or "PickedUp").SelectMany(job =>
                new[] {
                    (Job: job, Kind: "Pickup", Address: job.PickupAddress, Point: new GeoPoint(job.PickupLatitude, job.PickupLongitude), Position: job.PickupPosition),
                    (Job: job, Kind: "Drop-off", Address: job.DropoffAddress, Point: new GeoPoint(job.DropoffLatitude, job.DropoffLongitude), Position: job.DropoffPosition)
                }.Where(stop => stop.Kind != "Pickup" || job.Status == "Assigned"))
                .OrderBy(x => x.Job.AssignedAtUtc).ThenBy(x => x.Job.RouteId).ThenBy(x => x.Position ?? int.MaxValue)
                .ThenBy(x => x.Job.Id).ThenByDescending(x => x.Kind == "Pickup").ToArray();
            if (stops.Length == 0) return null;
            var routing = services.SingleOrDefault() ?? throw new InvalidOperationException("Routing unavailable.");
            var legs = new List<DriverNavigationStop>();
            var origin = request.Origin;
            foreach (var stop in stops)
            {
                var leg = origin == stop.Point ? new RoadRoute(0, 0, "")
                    : await routing.GetDirectionsAsync(origin, stop.Point, token)
                        ?? throw new ArgumentException("No road route to an assigned stop.");
                legs.Add(new(stop.Job.Id, stop.Kind, stop.Address, stop.Point, leg));
                origin = stop.Point;
            }
            var first = legs[0];
            return new(first.DeliveryId, first.StopKind, first.Address, first.Destination, first.Route, legs);
        }
        var next = assigned.OrderBy(x => x.AssignedAtUtc)
            .ThenBy(x => x.Status == "PickedUp" ? x.DropoffPosition ?? int.MaxValue : x.PickupPosition ?? int.MaxValue)
            .ThenBy(x => x.Id).FirstOrDefault();
        if (next is null) return null;
        var pickup = next.Status != "PickedUp";
        var destination = pickup ? new GeoPoint(next.PickupLatitude, next.PickupLongitude) : new GeoPoint(next.DropoffLatitude, next.DropoffLongitude);
        var provider = services.SingleOrDefault() ?? throw new InvalidOperationException("Routing unavailable.");
        var route = await provider.GetDirectionsAsync(request.Origin, destination, token);
        if (route is null) throw new ArgumentException("No road route to the next stop.");
        return new(next.Id, pickup ? "Pickup" : "Drop-off", pickup ? next.PickupAddress : next.DropoffAddress, destination, route);
    }
}
