using Dalan.Application.Drivers;
using Dalan.Application.Vehicles;
using Dalan.Infrastructure.Repositories;
using Xunit;

namespace Dalan.IntegrationTests;

[Collection("SQL Server")]
public sealed class FleetAdministrationTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Vehicle_and_driver_changes_persist_and_conflicting_assignments_are_rejected()
    {
        Guid vehicleId;
        Guid firstDriverId;
        Guid secondDriverId;
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var future = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1);

        await using (var context = fixture.CreateContext())
        {
            var vehicles = new VehicleService(new VehicleRepository(context));
            var created = await vehicles.RegisterAsync(new RegisterVehicleCommand(
                "V-" + suffix, "Van", 750, 4, future));
            Assert.Equal(VehicleRegistrationStatus.Registered, created.Status);
            vehicleId = created.Vehicle!.Id;
        }
        await using (var context = fixture.CreateContext())
        {
            var vehicles = new VehicleService(new VehicleRepository(context));
            var saved = await vehicles.GetByIdAsync(vehicleId);
            Assert.Equal("Van", saved!.Type);
            Assert.Equal(750, saved.PayloadCapacityKg);
            Assert.Equal(4, saved.SeatCapacity);
            Assert.Equal(future, saved.NextMaintenanceDate);
        }
        await using (var context = fixture.CreateContext())
        {
            var service = new DriverService(new DriverRepository(context),
                new VehicleRepository(context));
            var first = await service.RegisterAsync(new RegisterDriverCommand(
                "Jamie Cruz", "09123456789", "LIC-A-" + suffix, future));
            Assert.Equal(DriverMutationStatus.Created, first.Status);
            firstDriverId = first.Driver!.Id;
            var second = await service.RegisterAsync(new RegisterDriverCommand(
                "Sam Reyes", "09987654321", "LIC-B-" + suffix, future));
            Assert.Equal(DriverMutationStatus.Created, second.Status);
            secondDriverId = second.Driver!.Id;
            var duplicate = await service.RegisterAsync(new RegisterDriverCommand(
                "Other", "09111111111", "lic-a-" + suffix, future));
            Assert.Equal(DriverMutationStatus.Conflict, duplicate.Status);
        }
        await using (var context = fixture.CreateContext())
        {
            var service = new DriverService(new DriverRepository(context),
                new VehicleRepository(context));
            var assigned = await service.UpdateAsync(firstDriverId, new UpdateDriverCommand(
                "Jamie Cruz", "09123456789", "LIC-A-" + suffix, future,
                "Available", vehicleId));
            Assert.Equal(DriverMutationStatus.Updated, assigned.Status);
            Assert.Equal(vehicleId, assigned.Driver!.AssignedVehicleId);
        }
        await using (var context = fixture.CreateContext())
        {
            var service = new DriverService(new DriverRepository(context),
                new VehicleRepository(context));
            var conflict = await service.UpdateAsync(secondDriverId, new UpdateDriverCommand(
                "Sam Reyes", "09987654321", "LIC-B-" + suffix, future,
                "Available", vehicleId));
            Assert.Equal(DriverMutationStatus.Conflict, conflict.Status);
            Assert.Contains("already assigned", conflict.Error);
        }
        await using (var context = fixture.CreateContext())
        {
            var vehicles = new VehicleService(new VehicleRepository(context));
            Assert.Equal(VehicleMutationStatus.Conflict,
                (await vehicles.DeactivateAsync(vehicleId)).Status);
            Assert.Equal(VehicleMutationStatus.Conflict,
                (await vehicles.UpdateAsync(vehicleId, new UpdateVehicleCommand(
                    "Van", 750, 4, "Maintenance", future))).Status);
        }
        await using (var context = fixture.CreateContext())
        {
            var service = new DriverService(new DriverRepository(context),
                new VehicleRepository(context));
            var released = await service.DeactivateAsync(firstDriverId);
            Assert.Equal(DriverMutationStatus.Updated, released.Status);
            Assert.Null(released.Driver!.AssignedVehicleId);
        }
        await using (var context = fixture.CreateContext())
        {
            var vehicles = new VehicleService(new VehicleRepository(context));
            var inactive = await vehicles.DeactivateAsync(vehicleId);
            Assert.Equal(VehicleMutationStatus.Updated, inactive.Status);
            Assert.Equal("Inactive", inactive.Vehicle!.Availability);
        }
        await using (var context = fixture.CreateContext())
        {
            var service = new DriverService(new DriverRepository(context),
                new VehicleRepository(context));
            var unavailable = await service.UpdateAsync(secondDriverId, new UpdateDriverCommand(
                "Sam Reyes", "09987654321", "LIC-B-" + suffix, future,
                "Available", vehicleId));
            Assert.Equal(DriverMutationStatus.Conflict, unavailable.Status);
        }
    }
}
