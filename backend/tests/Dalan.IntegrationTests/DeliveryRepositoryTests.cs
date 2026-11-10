using Dalan.Application.Deliveries;
using Dalan.Application.Drivers;
using Dalan.Application.Vehicles;
using Dalan.Domain.Deliveries;
using Dalan.Infrastructure.Repositories;
using Xunit;

namespace Dalan.IntegrationTests;

[Collection("SQL Server")]
public sealed class DeliveryRepositoryTests(SqlServerFixture fixture)
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 11, 1, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Delivery_lifecycle_and_proof_are_persisted()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var future = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1);
        Guid vehicleId;
        Guid driverId;
        Guid deliveryId;

        await using (var context = fixture.CreateContext())
        {
            var vehicles = new VehicleService(new VehicleRepository(context));
            var vehicle = await vehicles.RegisterAsync(new(
                "DEL-" + suffix, "Van", 500, null, future));
            vehicleId = vehicle.Vehicle!.Id;

            var drivers = new DriverService(new DriverRepository(context),
                new VehicleRepository(context));
            var driver = await drivers.RegisterAsync(new(
                "Delivery Driver", "09123456789", "DEL-LIC-" + suffix, future));
            driverId = driver.Driver!.Id;
        }

        await using (var context = fixture.CreateContext())
        {
            var drivers = new DriverService(new DriverRepository(context),
                new VehicleRepository(context));
            var assigned = await drivers.UpdateAsync(driverId, new(
                "Delivery Driver", "09123456789", "DEL-LIC-" + suffix,
                future, "Available", vehicleId));
            Assert.Equal(DriverMutationStatus.Updated, assigned.Status);
        }

        await using (var context = fixture.CreateContext())
        {
            var repository = new DeliveryRepository(context);
            var delivery = CreateDelivery();
            await repository.AddAsync(delivery);
            deliveryId = delivery.Id;
        }

        await using (var context = fixture.CreateContext())
        {
            var repository = new DeliveryRepository(context);
            var delivery = await repository.GetByIdAsync(deliveryId);
            delivery!.Assign(driverId, vehicleId, CreatedAt.AddMinutes(1));
            Assert.Equal(SaveDeliveryOutcome.Saved, await repository.UpdateAsync(delivery));
        }

        await using (var context = fixture.CreateContext())
        {
            var repository = new DeliveryRepository(context);
            var delivery = await repository.GetByIdAsync(deliveryId);
            delivery!.MarkPickedUp(driverId, CreatedAt.AddMinutes(2));
            Assert.Equal(SaveDeliveryOutcome.Saved, await repository.UpdateAsync(delivery));
        }

        await using (var context = fixture.CreateContext())
        {
            var repository = new DeliveryRepository(context);
            var delivery = await repository.GetByIdAsync(deliveryId);
            delivery!.Complete(driverId, "proof/delivery.jpg", "Maria Santos",
                7.1, 125.6, CreatedAt.AddMinutes(3));
            Assert.Equal(SaveDeliveryOutcome.Saved, await repository.UpdateAsync(delivery));
        }

        await using (var context = fixture.CreateContext())
        {
            var saved = await new DeliveryRepository(context).GetByIdAsync(deliveryId);
            Assert.Equal(DeliveryStatus.Delivered, saved!.Status);
            Assert.Equal(driverId, saved.DriverId);
            Assert.Equal(vehicleId, saved.VehicleId);
            Assert.Equal("proof/delivery.jpg", saved.Proof!.PhotoReference);
            Assert.Equal("Maria Santos", saved.Proof.RecipientConfirmation);
            Assert.Empty(context.ChangeTracker.Entries());
        }
    }

    [Fact]
    public async Task Row_version_rejects_concurrent_delivery_changes()
    {
        Guid deliveryId;
        await using (var context = fixture.CreateContext())
        {
            var delivery = CreateDelivery();
            await new DeliveryRepository(context).AddAsync(delivery);
            deliveryId = delivery.Id;
        }

        await using var firstContext = fixture.CreateContext();
        await using var secondContext = fixture.CreateContext();
        var firstRepository = new DeliveryRepository(firstContext);
        var secondRepository = new DeliveryRepository(secondContext);
        var first = await firstRepository.GetByIdAsync(deliveryId);
        var second = await secondRepository.GetByIdAsync(deliveryId);
        first!.Cancel(CreatedAt.AddMinutes(1));
        second!.Cancel(CreatedAt.AddMinutes(2));

        Assert.Equal(SaveDeliveryOutcome.Saved, await firstRepository.UpdateAsync(first));
        Assert.Equal(SaveDeliveryOutcome.Conflict, await secondRepository.UpdateAsync(second));
    }

    private static Delivery CreateDelivery()
        => Delivery.Create("Juan Dela Cruz", "Warehouse", 7.1, 125.6,
            "Recipient address", 7.2, 125.7, CreatedAt, 250, CreatedAt.AddHours(4));
}
