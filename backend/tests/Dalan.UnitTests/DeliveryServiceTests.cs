using Dalan.Application.Deliveries;
using Dalan.Application.Drivers;
using Dalan.Application.Vehicles;
using Dalan.Domain.Deliveries;
using Dalan.Domain.Drivers;
using Dalan.Domain.Vehicles;
using Xunit;

namespace Dalan.UnitTests;

public sealed class DeliveryServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 1, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_validates_and_persists_pending_delivery()
    {
        var deliveries = new FakeDeliveryRepository();
        var service = CreateService(deliveries);
        using var source = new CancellationTokenSource();

        var result = await service.CreateAsync(ValidCreateCommand(), source.Token);

        Assert.Equal(DeliveryMutationStatus.Created, result.Status);
        Assert.Equal(DeliveryStatus.Pending, result.Delivery!.Status);
        Assert.Equal(Now, result.Delivery.CreatedAtUtc);
        Assert.Equal(source.Token, deliveries.Token);
        Assert.NotNull(deliveries.Stored);

        var invalid = await service.CreateAsync(ValidCreateCommand() with { RecipientName = " " });
        Assert.Equal(DeliveryMutationStatus.Invalid, invalid.Status);
    }

    [Fact]
    public async Task Assignment_requires_matching_available_driver_and_vehicle()
    {
        var vehicle = Vehicle.Register("ABC-123", payloadCapacityKg: 500);
        var driver = CreateDriver(vehicle.Id);
        var delivery = CreateDelivery(payloadWeightKg: 250);
        var deliveries = new FakeDeliveryRepository { Stored = delivery };
        var service = CreateService(deliveries,
            new FakeDriverRepository { Stored = driver },
            new FakeVehicleRepository { Stored = vehicle });

        var assigned = await service.AssignAsync(delivery.Id,
            new(driver.Id, vehicle.Id));

        Assert.Equal(DeliveryMutationStatus.Updated, assigned.Status);
        Assert.Equal(DeliveryStatus.Assigned, assigned.Delivery!.Status);
        Assert.Equal(driver.Id, assigned.Delivery.DriverId);

        var wrongVehicle = await service.AssignAsync(delivery.Id,
            new(driver.Id, Guid.NewGuid()));
        Assert.Equal(DeliveryMutationStatus.Invalid, wrongVehicle.Status);
    }

    [Fact]
    public async Task Assignment_rejects_payload_over_vehicle_capacity()
    {
        var vehicle = Vehicle.Register("ABC-124", payloadCapacityKg: 100);
        var driver = CreateDriver(vehicle.Id);
        var delivery = CreateDelivery(payloadWeightKg: 150);
        var service = CreateService(
            new FakeDeliveryRepository { Stored = delivery },
            new FakeDriverRepository { Stored = driver },
            new FakeVehicleRepository { Stored = vehicle });

        var result = await service.AssignAsync(delivery.Id, new(driver.Id, vehicle.Id));

        Assert.Equal(DeliveryMutationStatus.Conflict, result.Status);
        Assert.Contains("payload capacity", result.Error);
    }

    [Fact]
    public async Task Assignment_rejects_driver_with_expired_license()
    {
        var vehicle = Vehicle.Register(
            "ABC-126",
            payloadCapacityKg: 500);

        var licenseExpiry =
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        var driver = Driver.Register(
            "Jamie Cruz",
            "09123456789",
            "LIC-EXPIRED",
            licenseExpiry);

        driver.AssignVehicle(vehicle.Id);

        var delivery = CreateDelivery();

        var timeAfterExpiry = new DateTimeOffset(
            licenseExpiry
                .AddDays(1)
                .ToDateTime(TimeOnly.MinValue),
            TimeSpan.Zero);

        var service = new DeliveryService(
            new FakeDeliveryRepository
            {
                Stored = delivery
            },
            new FakeDriverRepository
            {
                Stored = driver
            },
            new FakeVehicleRepository
            {
                Stored = vehicle
            },
            new FixedTimeProvider(timeAfterExpiry));

        var result = await service.AssignAsync(
            delivery.Id,
            new AssignDeliveryCommand(
                driver.Id,
                vehicle.Id));

        Assert.Equal(
            DeliveryMutationStatus.Conflict,
            result.Status);

        Assert.Contains(
            "current license",
            result.Error);

        Assert.Equal(
            DeliveryStatus.Pending,
            delivery.Status);
    }

    [Fact]
    public async Task Pickup_and_completion_return_proof_details()
    {
        var vehicle = Vehicle.Register("ABC-125", payloadCapacityKg: 500);
        var driver = CreateDriver(vehicle.Id);
        var delivery = CreateDelivery();
        delivery.Assign(driver.Id, vehicle.Id, Now);
        var deliveries = new FakeDeliveryRepository { Stored = delivery };
        var service = CreateService(deliveries);

        Assert.Equal(DeliveryMutationStatus.Updated,
            (await service.MarkPickedUpAsync(delivery.Id, driver.Id)).Status);
        var completed = await service.CompleteAsync(delivery.Id,
            new(driver.Id, "proof/photo.jpg", "Maria Santos", 7.1, 125.6));

        Assert.Equal(DeliveryMutationStatus.Updated, completed.Status);
        Assert.Equal(DeliveryStatus.Delivered, completed.Delivery!.Status);
        Assert.Equal("proof/photo.jpg", completed.Delivery.Proof!.PhotoReference);
    }

    [Fact]
    public async Task Save_conflict_is_reported_without_success_payload()
    {
        var delivery = CreateDelivery();
        var deliveries = new FakeDeliveryRepository
        {
            Stored = delivery,
            Outcome = SaveDeliveryOutcome.Conflict
        };
        var result = await CreateService(deliveries).CancelAsync(delivery.Id);

        Assert.Equal(DeliveryMutationStatus.Conflict, result.Status);
        Assert.Null(result.Delivery);
    }

    private static DeliveryService CreateService(
        FakeDeliveryRepository? deliveries = null,
        FakeDriverRepository? drivers = null,
        FakeVehicleRepository? vehicles = null)
        => new(deliveries ?? new(), drivers ?? new(), vehicles ?? new(),
            new FixedTimeProvider(Now));

    private static CreateDeliveryCommand ValidCreateCommand()
        => new("Juan Dela Cruz", "Warehouse", 7.1, 125.6,
            "Recipient address", 7.2, 125.7, 250, Now.AddHours(4));

    private static Delivery CreateDelivery(decimal? payloadWeightKg = null)
        => Delivery.Create("Juan Dela Cruz", "Warehouse", 7.1, 125.6,
            "Recipient address", 7.2, 125.7, Now, payloadWeightKg);

    private static Driver CreateDriver(Guid vehicleId)
    {
        var driver = Driver.Register("Jamie Cruz", "09123456789", "LIC-123",
            DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1));
        driver.AssignVehicle(vehicleId);
        return driver;
    }

    private sealed class FixedTimeProvider(DateTimeOffset current) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => current;
    }

    private sealed class FakeDeliveryRepository : IDeliveryRepository
    {
        public Delivery? Stored { get; set; }
        public SaveDeliveryOutcome Outcome { get; init; } = SaveDeliveryOutcome.Saved;
        public CancellationToken Token { get; private set; }

        public Task AddAsync(Delivery delivery, CancellationToken cancellationToken = default)
        {
            Stored = delivery;
            Token = cancellationToken;
            return Task.CompletedTask;
        }

        public Task<Delivery?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;
            return Task.FromResult(Stored?.Id == id ? Stored : null);
        }

        public Task<IReadOnlyList<Delivery>> ListAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Delivery>>(Stored is null ? [] : [Stored]);

        public Task<SaveDeliveryOutcome> UpdateAsync(
            Delivery delivery, CancellationToken cancellationToken = default)
        {
            Stored = delivery;
            Token = cancellationToken;
            return Task.FromResult(Outcome);
        }
    }

    private sealed class FakeDriverRepository : IDriverRepository
    {
        public Driver? Stored { get; init; }
        public Task<SaveDriverOutcome> AddAsync(Driver driver,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Driver?> GetByIdAsync(Guid id,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Stored?.Id == id ? Stored : null);
        public Task<IReadOnlyList<Driver>> ListAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SaveDriverOutcome> UpdateAsync(Driver driver,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeVehicleRepository : IVehicleRepository
    {
        public Vehicle? Stored { get; init; }
        public Task<AddVehicleOutcome> AddAsync(Vehicle vehicle,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Vehicle?> GetByIdAsync(Guid id,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Stored?.Id == id ? Stored : null);
        public Task<IReadOnlyList<Vehicle>> ListAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(Vehicle vehicle,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasAssignedDriverAsync(Guid id,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
