namespace Dalan.Application.Vehicles;

public sealed record RegisterVehicleCommand(
    string? RegistrationNumber, string? Type = null, decimal? PayloadCapacityKg = null,
    int? SeatCapacity = null, DateOnly? NextMaintenanceDate = null);
public sealed record UpdateVehicleCommand(
    string? Type, decimal? PayloadCapacityKg, int? SeatCapacity,
    string Availability, DateOnly? NextMaintenanceDate);
public sealed record VehicleDetails(
    Guid Id, string RegistrationNumber, string? Type = null,
    decimal? PayloadCapacityKg = null, int? SeatCapacity = null,
    string Availability = "Available", DateOnly? NextMaintenanceDate = null);
public enum VehicleRegistrationStatus { Registered, Invalid, Duplicate }
public sealed record VehicleRegistrationResult(
    VehicleRegistrationStatus Status,
    VehicleDetails? Vehicle = null,
    string? Error = null);
public enum VehicleMutationStatus { Updated, NotFound, Invalid, Conflict }
public sealed record VehicleMutationResult(
    VehicleMutationStatus Status, VehicleDetails? Vehicle = null, string? Error = null);

public interface IVehicleService
{
    Task<VehicleRegistrationResult> RegisterAsync(
        RegisterVehicleCommand command,
        CancellationToken cancellationToken = default);

    Task<VehicleDetails?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VehicleDetails>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<VehicleMutationResult> UpdateAsync(
        Guid id, UpdateVehicleCommand command, CancellationToken cancellationToken = default);

    Task<VehicleMutationResult> DeactivateAsync(
        Guid id, CancellationToken cancellationToken = default);
}
