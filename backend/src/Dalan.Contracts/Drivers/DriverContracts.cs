using System.ComponentModel.DataAnnotations;

namespace Dalan.Contracts.Drivers;

public sealed record RegisterDriverRequest(
    [Required] string? Name, [Required] string? Phone,
    [Required] string? LicenseNumber, [Required] DateOnly? LicenseExpiry);

public sealed record UpdateDriverRequest(
    [Required] string? Name, [Required] string? Phone,
    [Required] string? LicenseNumber, [Required] DateOnly? LicenseExpiry,
    [Required] string? Availability, Guid? AssignedVehicleId);

public sealed record DriverResponse(
    Guid Id, string Name, string Phone, string LicenseNumber,
    DateOnly LicenseExpiry, string Availability, Guid? AssignedVehicleId);
