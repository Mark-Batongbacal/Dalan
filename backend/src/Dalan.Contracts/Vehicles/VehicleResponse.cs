namespace Dalan.Contracts.Vehicles;

public sealed record VehicleResponse(
    Guid Id, string RegistrationNumber, string? Type = null,
    decimal? PayloadCapacityKg = null, int? SeatCapacity = null,
    string Availability = "Available", DateOnly? NextMaintenanceDate = null);
