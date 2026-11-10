namespace Dalan.Web.Models;

public sealed record VehicleListItemViewModel(
    Guid Id,
    string RegistrationNumber,
    string? Type,
    decimal? PayloadCapacityKg,
    int? SeatCapacity,
    string Availability,
    DateOnly? NextMaintenanceDate);

public sealed record FleetRegistryViewModel(
    IReadOnlyList<VehicleListItemViewModel> Vehicles);
