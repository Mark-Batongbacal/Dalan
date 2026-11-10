using Dalan.Domain.Deliveries;
using Xunit;

namespace Dalan.UnitTests;

public sealed class DeliveryTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 11, 1, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Creation_normalizes_values_and_starts_pending()
    {
        var expectedAt = CreatedAt.AddHours(4);
        var delivery = Create(payloadWeightKg: 250, expectedDeliveryAt: expectedAt);

        Assert.NotEqual(Guid.Empty, delivery.Id);
        Assert.Equal("Juan Dela Cruz", delivery.RecipientName);
        Assert.Equal("Davao City Warehouse", delivery.PickupAddress);
        Assert.Equal("Matina, Davao City", delivery.DropoffAddress);
        Assert.Equal(250, delivery.PayloadWeightKg);
        Assert.Equal(expectedAt, delivery.ExpectedDeliveryAtUtc);
        Assert.Equal(DeliveryStatus.Pending, delivery.Status);
        Assert.Null(delivery.DriverId);
        Assert.Null(delivery.Proof);
    }

    [Theory]
    [InlineData(-91, 125)]
    [InlineData(91, 125)]
    [InlineData(7, -181)]
    [InlineData(7, 181)]
    [InlineData(double.NaN, 125)]
    [InlineData(7, double.PositiveInfinity)]
    public void Invalid_coordinates_are_rejected(double latitude, double longitude)
        => Assert.Throws<ArgumentOutOfRangeException>(() => Delivery.Create(
            "Recipient", "Pickup", latitude, longitude,
            "Dropoff", 7, 125, CreatedAt));

    [Fact]
    public void Invalid_text_weight_and_schedule_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => Delivery.Create(
            " ", "Pickup", 7, 125, "Dropoff", 7, 125, CreatedAt));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(payloadWeightKg: 0));
        Assert.Throws<ArgumentException>(() => Create(expectedDeliveryAt: CreatedAt));
    }

    [Fact]
    public void Legal_lifecycle_records_assignment_pickup_and_combined_proof()
    {
        var delivery = Create();
        var driverId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();

        delivery.Assign(driverId, vehicleId, CreatedAt.AddMinutes(1));
        delivery.MarkPickedUp(driverId, CreatedAt.AddMinutes(2));
        delivery.Complete(driverId, "proof/photo-1.jpg", "Maria Santos",
            7.0636, 125.6065, CreatedAt.AddMinutes(3));

        Assert.Equal(DeliveryStatus.Delivered, delivery.Status);
        Assert.Equal(driverId, delivery.DriverId);
        Assert.Equal(vehicleId, delivery.VehicleId);
        Assert.Equal(CreatedAt.AddMinutes(1), delivery.AssignedAtUtc);
        Assert.Equal(CreatedAt.AddMinutes(2), delivery.PickedUpAtUtc);
        Assert.Equal(CreatedAt.AddMinutes(3), delivery.DeliveredAtUtc);
        Assert.Equal("proof/photo-1.jpg", delivery.Proof!.PhotoReference);
        Assert.Equal("Maria Santos", delivery.Proof.RecipientConfirmation);
    }

    [Fact]
    public void Only_assigned_driver_can_pick_up_or_complete_delivery()
    {
        var delivery = Create();
        var assignedDriver = Guid.NewGuid();
        delivery.Assign(assignedDriver, Guid.NewGuid(), CreatedAt.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() =>
            delivery.MarkPickedUp(Guid.NewGuid(), CreatedAt.AddMinutes(2)));
        delivery.MarkPickedUp(assignedDriver, CreatedAt.AddMinutes(2));
        Assert.Throws<InvalidOperationException>(() => delivery.Complete(
            Guid.NewGuid(), "photo.jpg", "Recipient", 7, 125, CreatedAt.AddMinutes(3)));
    }

    [Fact]
    public void Cancellation_is_allowed_only_before_pickup()
    {
        var pending = Create();
        pending.Cancel(CreatedAt.AddMinutes(1));
        Assert.Equal(DeliveryStatus.Cancelled, pending.Status);
        Assert.Throws<InvalidOperationException>(() => pending.Cancel(CreatedAt.AddMinutes(2)));

        var pickedUp = Create();
        var driverId = Guid.NewGuid();
        pickedUp.Assign(driverId, Guid.NewGuid(), CreatedAt.AddMinutes(1));
        pickedUp.MarkPickedUp(driverId, CreatedAt.AddMinutes(2));
        Assert.Throws<InvalidOperationException>(() => pickedUp.Cancel(CreatedAt.AddMinutes(3)));
    }

    [Fact]
    public void Lifecycle_timestamps_cannot_move_backwards()
    {
        var delivery = Create();
        var driverId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        delivery.Assign(driverId, vehicleId, CreatedAt.AddMinutes(5));

        Assert.Throws<ArgumentException>(() =>
            delivery.Assign(Guid.NewGuid(), Guid.NewGuid(), CreatedAt.AddMinutes(4)));
        Assert.Equal(driverId, delivery.DriverId);
        Assert.Equal(vehicleId, delivery.VehicleId);
        Assert.Equal(CreatedAt.AddMinutes(5), delivery.AssignedAtUtc);
        Assert.Throws<ArgumentException>(() => delivery.Cancel(CreatedAt.AddMinutes(4)));
        Assert.Throws<ArgumentException>(() =>
            delivery.MarkPickedUp(driverId, CreatedAt.AddMinutes(4)));
    }

    [Fact]
    public void Completion_requires_all_proof_fields()
    {
        var delivery = Create();
        var driverId = Guid.NewGuid();
        delivery.Assign(driverId, Guid.NewGuid(), CreatedAt.AddMinutes(1));
        delivery.MarkPickedUp(driverId, CreatedAt.AddMinutes(2));

        Assert.Throws<ArgumentException>(() => delivery.Complete(
            driverId, " ", "Maria Santos", 7, 125, CreatedAt.AddMinutes(3)));
        Assert.Throws<ArgumentException>(() => delivery.Complete(
            driverId, "proof/photo.jpg", " ", 7, 125, CreatedAt.AddMinutes(3)));
        Assert.Equal(DeliveryStatus.PickedUp, delivery.Status);
        Assert.Null(delivery.Proof);
    }

    private static Delivery Create(
        decimal? payloadWeightKg = null,
        DateTimeOffset? expectedDeliveryAt = null)
        => Delivery.Create(
            "  Juan Dela Cruz  ",
            "  Davao City Warehouse  ",
            7.0731,
            125.6128,
            "  Matina, Davao City  ",
            7.0636,
            125.6065,
            CreatedAt,
            payloadWeightKg,
            expectedDeliveryAt);
}
