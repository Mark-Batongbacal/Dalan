namespace Dalan.Contracts.FleetOptimization;

public sealed record CoordinateRequest(double Latitude, double Longitude);
public sealed record VehicleStartRequest(Guid VehicleId, CoordinateRequest Start);
public sealed record FleetJobRequest(
    string JobId, CoordinateRequest Pickup, CoordinateRequest Dropoff,
    Guid? AssignedVehicleId = null);
public sealed record FleetOptimizeRequest(
    IReadOnlyList<VehicleStartRequest> Vehicles,
    IReadOnlyList<FleetJobRequest> Jobs);
