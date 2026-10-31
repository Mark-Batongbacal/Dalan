using System.ComponentModel.DataAnnotations;

namespace Dalan.Contracts.Vehicles;

public sealed record RegisterVehicleRequest([Required] string? RegistrationNumber);
