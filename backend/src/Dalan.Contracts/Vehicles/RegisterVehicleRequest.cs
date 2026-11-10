using System.ComponentModel.DataAnnotations;

namespace Dalan.Contracts.Vehicles;

public sealed record RegisterVehicleRequest(
    [Required] string? RegistrationNumber,
    string? Type = null, decimal? PayloadCapacityKg = null,
    int? SeatCapacity = null, DateOnly? NextMaintenanceDate = null);

public sealed record UpdateVehicleRequest(
    string? Type, decimal? PayloadCapacityKg, int? SeatCapacity,
    [Required] string? Availability, DateOnly? NextMaintenanceDate);
