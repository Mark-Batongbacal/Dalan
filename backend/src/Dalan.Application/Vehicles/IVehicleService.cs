namespace Dalan.Application.Vehicles;

public sealed record RegisterVehicleCommand(string? RegistrationNumber);
public sealed record VehicleDetails(Guid Id, string RegistrationNumber);
public enum VehicleRegistrationStatus { Registered, Invalid, Duplicate }
public sealed record VehicleRegistrationResult(
    VehicleRegistrationStatus Status, VehicleDetails? Vehicle = null, string? Error = null);

public interface IVehicleService
{
    Task<VehicleRegistrationResult> RegisterAsync(
        RegisterVehicleCommand command, CancellationToken cancellationToken = default);
    Task<VehicleDetails?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
