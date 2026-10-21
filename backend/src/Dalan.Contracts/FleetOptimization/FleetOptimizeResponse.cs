namespace Dalan.Contracts.FleetOptimization;

public sealed record FleetStopResponse(string JobId, string Kind, CoordinateRequest Location);
public sealed record OptimizedVehicleRouteResponse(
    Guid VehicleId, IReadOnlyList<FleetStopResponse> Stops,
    double DistanceMeters, double DurationSeconds);
public sealed record FleetOptimizeResponse(
    IReadOnlyList<OptimizedVehicleRouteResponse> Routes,
    IReadOnlyList<string> UnassignedJobIds);
