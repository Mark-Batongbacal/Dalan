using Dalan.Application.Routing;
namespace Dalan.Application.DriverExecution;
public sealed record DriverNavigationRequest(GeoPoint Origin, DateTimeOffset CapturedAtUtc, double AccuracyMeters);
public sealed record DriverNavigationRoute(Guid DeliveryId, string StopKind, string Address, GeoPoint Destination, RoadRoute Route);
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
        var next = (await tasks.ListAsync(driverId, token)).OrderBy(x => x.AssignedAtUtc)
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
