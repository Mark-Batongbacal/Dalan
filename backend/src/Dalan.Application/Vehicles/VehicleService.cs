using Dalan.Domain.Vehicles;

namespace Dalan.Application.Vehicles;

public sealed class VehicleService(IVehicleRepository repository) : IVehicleService
{
    public async Task<VehicleRegistrationResult> RegisterAsync(
        RegisterVehicleCommand command,
        CancellationToken cancellationToken = default)
    {
        Vehicle vehicle;
        try
        {
            vehicle = Vehicle.Register(command.RegistrationNumber);
        }
        catch (ArgumentException exception)
        {
            return new(VehicleRegistrationStatus.Invalid, Error: exception.Message);
        }

        var outcome = await repository.AddAsync(vehicle, cancellationToken);
        return outcome == AddVehicleOutcome.DuplicateRegistration
            ? new(VehicleRegistrationStatus.Duplicate, Error: "A vehicle with this registration number already exists.")
            : new(VehicleRegistrationStatus.Registered, ToDetails(vehicle));
    }

    public async Task<VehicleDetails?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var vehicle = await repository.GetByIdAsync(id, cancellationToken);
        return vehicle is null ? null : ToDetails(vehicle);
    }

    public async Task<IReadOnlyList<VehicleDetails>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var vehicles = await repository.ListAsync(cancellationToken);
        return vehicles.Select(ToDetails).ToArray();
    }

    private static VehicleDetails ToDetails(Vehicle vehicle)
        => new(vehicle.Id, vehicle.RegistrationNumber);
}