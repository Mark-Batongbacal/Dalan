using Dalan.Application.Vehicles;
using Dalan.Domain.Drivers;
using Dalan.Domain.Vehicles;

namespace Dalan.Application.Drivers;

public sealed class DriverService(IDriverRepository drivers, IVehicleRepository vehicles) : IDriverService
{
    public async Task<DriverMutationResult> RegisterAsync(RegisterDriverCommand command,
        CancellationToken cancellationToken = default)
    {
        Driver driver;
        try { driver = Driver.Register(command.Name, command.Phone,
            command.LicenseNumber, command.LicenseExpiry); }
        catch (ArgumentException error)
        { return new(DriverMutationStatus.Invalid, Error: error.Message); }

        var saved = await drivers.AddAsync(driver, cancellationToken);
        return saved == SaveDriverOutcome.Saved
            ? new(DriverMutationStatus.Created, ToDetails(driver))
            : new(DriverMutationStatus.Conflict, Error: "A driver with this license number already exists.");
    }

    public async Task<DriverMutationResult> UpdateAsync(Guid id, UpdateDriverCommand command,
        CancellationToken cancellationToken = default)
    {
        var driver = await drivers.GetByIdAsync(id, cancellationToken);
        if (driver is null) return new(DriverMutationStatus.NotFound);
        if (!Enum.TryParse<DriverAvailability>(command.Availability, ignoreCase: true, out var availability)
            || !Enum.IsDefined(availability))
            return new(DriverMutationStatus.Invalid, Error: "Choose a valid driver availability.");
        if (availability != DriverAvailability.Available && command.AssignedVehicleId is not null)
            return new(DriverMutationStatus.Invalid,
                Error: "An unavailable driver cannot keep a vehicle assignment.");

        if (command.AssignedVehicleId is Guid vehicleId)
        {
            var vehicle = await vehicles.GetByIdAsync(vehicleId, cancellationToken);
            if (vehicle is null)
                return new(DriverMutationStatus.Invalid, Error: "Choose a registered vehicle.");
            if (vehicle.Availability != VehicleAvailability.Available)
                return new(DriverMutationStatus.Conflict,
                    Error: "Only available vehicles can be assigned.");
        }

        try
        {
            driver.UpdateProfile(command.Name, command.Phone, command.LicenseNumber,
                command.LicenseExpiry, availability);
            driver.AssignVehicle(command.AssignedVehicleId);
        }
        catch (ArgumentException error)
        { return new(DriverMutationStatus.Invalid, Error: error.Message); }
        catch (InvalidOperationException error)
        { return new(DriverMutationStatus.Conflict, Error: error.Message); }

        return MapSave(await drivers.UpdateAsync(driver, cancellationToken), driver);
    }

    public async Task<DriverMutationResult> DeactivateAsync(Guid id,
        CancellationToken cancellationToken = default)
    {
        var driver = await drivers.GetByIdAsync(id, cancellationToken);
        if (driver is null) return new(DriverMutationStatus.NotFound);
        driver.Deactivate();
        return MapSave(await drivers.UpdateAsync(driver, cancellationToken), driver);
    }

    public async Task<DriverDetails?> GetByIdAsync(Guid id,
        CancellationToken cancellationToken = default)
    {
        var driver = await drivers.GetByIdAsync(id, cancellationToken);
        return driver is null ? null : ToDetails(driver);
    }

    public async Task<IReadOnlyList<DriverDetails>> ListAsync(
        CancellationToken cancellationToken = default)
        => (await drivers.ListAsync(cancellationToken)).Select(ToDetails).ToArray();

    private static DriverMutationResult MapSave(SaveDriverOutcome saved, Driver driver)
        => saved switch
        {
            SaveDriverOutcome.Saved => new(DriverMutationStatus.Updated, ToDetails(driver)),
            SaveDriverOutcome.Missing => new(DriverMutationStatus.NotFound),
            SaveDriverOutcome.DuplicateLicense => new(DriverMutationStatus.Conflict,
                Error: "A driver with this license number already exists."),
            SaveDriverOutcome.VehicleAssigned => new(DriverMutationStatus.Conflict,
                Error: "This vehicle is already assigned to another driver."),
            _ => throw new ArgumentOutOfRangeException(nameof(saved))
        };

    private static DriverDetails ToDetails(Driver driver)
        => new(driver.Id, driver.Name, driver.Phone, driver.LicenseNumber,
            driver.LicenseExpiry, driver.Availability.ToString(), driver.AssignedVehicleId);
}
