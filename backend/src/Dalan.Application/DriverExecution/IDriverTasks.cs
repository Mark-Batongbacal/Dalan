using Dalan.Application.Routing;
using Dalan.Contracts.DriverExecution;

namespace Dalan.Application.DriverExecution;

public enum DriverEventResult { Applied, Missing, Conflict, Invalid }

public sealed record DriverRouteStops(GeoPoint Pickup, GeoPoint Dropoff);

public interface IDriverTasks
{
    Task<Guid?> FindDriverAsync(string userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<DriverTask>> ListAsync(Guid driverId, CancellationToken cancellationToken);
    Task<DriverRouteStops?> GetRouteStopsAsync(Guid driverId, Guid deliveryId, CancellationToken cancellationToken);
    Task<DriverEventResult> ApplyAsync(Guid driverId, Guid deliveryId, DriverTaskEvent request,
        CancellationToken cancellationToken);
    Task<byte[]?> GetProofPhotoAsync(Guid deliveryId, CancellationToken cancellationToken);
}
