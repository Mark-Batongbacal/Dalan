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
            vehicle = Vehicle.Register(command.RegistrationNumber, command.Type,
                command.PayloadCapacityKg, command.SeatCapacity, command.NextMaintenanceDate);
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

    public async Task<VehicleMutationResult> UpdateAsync(
        Guid id, UpdateVehicleCommand command, CancellationToken cancellationToken = default)
    {
        var vehicle = await repository.GetByIdAsync(id, cancellationToken);
        if (vehicle is null) return new(VehicleMutationStatus.NotFound);
        if (!Enum.TryParse<VehicleAvailability>(command.Availability, ignoreCase: true, out var availability)
            || !Enum.IsDefined(availability))
            return new(VehicleMutationStatus.Invalid, Error: "Choose a valid vehicle availability.");
        if (availability != VehicleAvailability.Available
            && await repository.HasAssignedDriverAsync(id, cancellationToken))
            return new(VehicleMutationStatus.Conflict,
                Error: "Release the assigned driver before making this vehicle unavailable.");
        try
        {
            vehicle.UpdateDetails(command.Type, command.PayloadCapacityKg,
                command.SeatCapacity, availability, command.NextMaintenanceDate);
        }
        catch (ArgumentException error)
        {
            return new(VehicleMutationStatus.Invalid, Error: error.Message);
        }
        return await repository.UpdateAsync(vehicle, cancellationToken)
            ? new(VehicleMutationStatus.Updated, ToDetails(vehicle))
            : new(VehicleMutationStatus.NotFound);
    }

    public async Task<VehicleMutationResult> DeactivateAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        var vehicle = await repository.GetByIdAsync(id, cancellationToken);
        if (vehicle is null) return new(VehicleMutationStatus.NotFound);
        if (await repository.HasAssignedDriverAsync(id, cancellationToken))
            return new(VehicleMutationStatus.Conflict,
                Error: "Release the assigned driver before deactivating this vehicle.");
        vehicle.Deactivate();
        return await repository.UpdateAsync(vehicle, cancellationToken)
            ? new(VehicleMutationStatus.Updated, ToDetails(vehicle))
            : new(VehicleMutationStatus.NotFound);
    }

    private static VehicleDetails ToDetails(Vehicle vehicle)
        => new(vehicle.Id, vehicle.RegistrationNumber, vehicle.Type,
            vehicle.PayloadCapacityKg, vehicle.SeatCapacity,
            vehicle.Availability.ToString(), vehicle.NextMaintenanceDate);
}
