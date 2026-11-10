namespace Dalan.Application.Drivers;

public sealed record RegisterDriverCommand(
    string? Name, string? Phone, string? LicenseNumber, DateOnly LicenseExpiry);
public sealed record UpdateDriverCommand(
    string? Name, string? Phone, string? LicenseNumber, DateOnly LicenseExpiry,
    string Availability, Guid? AssignedVehicleId);
public sealed record DriverDetails(
    Guid Id, string Name, string Phone, string LicenseNumber, DateOnly LicenseExpiry,
    string Availability, Guid? AssignedVehicleId);
public enum DriverMutationStatus { Created, Updated, NotFound, Invalid, Conflict }
public sealed record DriverMutationResult(
    DriverMutationStatus Status, DriverDetails? Driver = null, string? Error = null);

public interface IDriverService
{
    Task<DriverMutationResult> RegisterAsync(RegisterDriverCommand command,
        CancellationToken cancellationToken = default);
    Task<DriverMutationResult> UpdateAsync(Guid id, UpdateDriverCommand command,
        CancellationToken cancellationToken = default);
    Task<DriverMutationResult> DeactivateAsync(Guid id,
        CancellationToken cancellationToken = default);
    Task<DriverDetails?> GetByIdAsync(Guid id,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DriverDetails>> ListAsync(
        CancellationToken cancellationToken = default);
}
