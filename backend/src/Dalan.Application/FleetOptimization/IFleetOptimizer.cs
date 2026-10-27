using Dalan.Application.Routing;

namespace Dalan.Application.FleetOptimization;

public sealed record FleetVehicle(Guid Id, GeoPoint Start);
public sealed record FleetJob(string Id, GeoPoint Pickup, GeoPoint Dropoff, Guid? AssignedVehicleId = null);
public sealed record FleetOptimizationRequest(
    IReadOnlyList<FleetVehicle> Vehicles, IReadOnlyList<FleetJob> Jobs);

public enum FleetStopKind { Pickup, Dropoff }
public sealed record FleetStop(string JobId, FleetStopKind Kind, GeoPoint Point);
public sealed record OptimizedVehicleRoute(
    Guid VehicleId, IReadOnlyList<FleetStop> Stops, double DistanceMeters, double DurationSeconds);
public sealed record FleetOptimizationResult(
    IReadOnlyList<OptimizedVehicleRoute> Routes, IReadOnlyList<string> UnassignedJobIds);

public interface IFleetOptimizer
{
    Task<FleetOptimizationResult> OptimizeAsync(
        FleetOptimizationRequest request, CancellationToken cancellationToken = default);
}
