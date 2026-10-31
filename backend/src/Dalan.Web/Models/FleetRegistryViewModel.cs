namespace Dalan.Web.Models;

public sealed record VehicleListItemViewModel(
    Guid Id,
    string RegistrationNumber);

public sealed record FleetRegistryViewModel(
    IReadOnlyList<VehicleListItemViewModel> Vehicles);