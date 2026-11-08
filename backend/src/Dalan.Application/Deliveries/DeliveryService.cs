using Dalan.Application.Drivers;
using Dalan.Application.Vehicles;
using Dalan.Domain.Deliveries;
using Dalan.Domain.Drivers;
using Dalan.Domain.Vehicles;

namespace Dalan.Application.Deliveries;

public sealed class DeliveryService(
    IDeliveryRepository deliveries,
    IDriverRepository drivers,
    IVehicleRepository vehicles,
    TimeProvider timeProvider) : IDeliveryService
{
    public async Task<DeliveryMutationResult> CreateAsync(
        CreateDeliveryCommand command, CancellationToken cancellationToken = default)
    {
        Delivery delivery;
        try
        {
            delivery = Delivery.Create(
                command.RecipientName,
                command.PickupAddress,
                command.PickupLatitude,
                command.PickupLongitude,
                command.DropoffAddress,
                command.DropoffLatitude,
                command.DropoffLongitude,
                timeProvider.GetUtcNow(),
                command.PayloadWeightKg,
                command.ExpectedDeliveryAtUtc);
        }
        catch (ArgumentException error)
        {
            return new(DeliveryMutationStatus.Invalid, Error: error.Message);
        }

        await deliveries.AddAsync(delivery, cancellationToken);
        return new(DeliveryMutationStatus.Created, ToDetails(delivery));
    }

    public async Task<DeliveryMutationResult> AssignAsync(
    Guid id,
    AssignDeliveryCommand command,
    CancellationToken cancellationToken = default)
    {
        var delivery = await deliveries.GetByIdAsync(id, cancellationToken);
        if (delivery is null)
            return new(DeliveryMutationStatus.NotFound);

        var driver = await drivers.GetByIdAsync(
            command.DriverId,
            cancellationToken);

        var vehicle = await vehicles.GetByIdAsync(
            command.VehicleId,
            cancellationToken);

        if (driver is null || vehicle is null)
        {
            return new(
                DeliveryMutationStatus.Invalid,
                Error: "Choose a registered driver and vehicle.");
        }

        var now = timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        if (driver.Availability != DriverAvailability.Available
            || vehicle.Availability != VehicleAvailability.Available)
        {
            return new(
                DeliveryMutationStatus.Conflict,
                Error: "Only available drivers and vehicles can be assigned.");
        }

        if (driver.LicenseExpiry < today)
        {
            return new(
                DeliveryMutationStatus.Conflict,
                Error: "The selected driver must have a current license.");
        }

        if (driver.AssignedVehicleId != vehicle.Id)
        {
            return new(
                DeliveryMutationStatus.Conflict,
                Error: "The selected vehicle must be assigned to the selected driver.");
        }

        if (delivery.PayloadWeightKg is decimal weight
            && vehicle.PayloadCapacityKg is decimal capacity
            && weight > capacity)
        {
            return new(
                DeliveryMutationStatus.Conflict,
                Error: "The delivery exceeds the selected vehicle's payload capacity.");
        }

        try
        {
            delivery.Assign(driver.Id, vehicle.Id, now);
        }
        catch (ArgumentException error)
        {
            return new(
                DeliveryMutationStatus.Invalid,
                Error: error.Message);
        }
        catch (InvalidOperationException error)
        {
            return new(
                DeliveryMutationStatus.Conflict,
                Error: error.Message);
        }

        var outcome = await deliveries.UpdateAsync(
            delivery,
            cancellationToken);

        return MapSave(outcome, delivery);
    }

    public Task<DeliveryMutationResult> MarkPickedUpAsync(
        Guid id, Guid driverId, CancellationToken cancellationToken = default)
        => MutateAsync(id, delivery => delivery.MarkPickedUp(
            driverId, timeProvider.GetUtcNow()), cancellationToken);

    public Task<DeliveryMutationResult> CompleteAsync(
        Guid id, CompleteDeliveryCommand command, CancellationToken cancellationToken = default)
        => MutateAsync(id, delivery => delivery.Complete(
            command.DriverId,
            command.PhotoReference,
            command.RecipientConfirmation,
            command.Latitude,
            command.Longitude,
            timeProvider.GetUtcNow()), cancellationToken);

    public Task<DeliveryMutationResult> CancelAsync(
        Guid id, CancellationToken cancellationToken = default)
        => MutateAsync(id, delivery => delivery.Cancel(
            timeProvider.GetUtcNow()), cancellationToken);

    public async Task<DeliveryDetails?> GetByIdAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        var delivery = await deliveries.GetByIdAsync(id, cancellationToken);
        return delivery is null ? null : ToDetails(delivery);
    }

    public async Task<IReadOnlyList<DeliveryDetails>> ListAsync(
        CancellationToken cancellationToken = default)
        => (await deliveries.ListAsync(cancellationToken)).Select(ToDetails).ToArray();

    private async Task<DeliveryMutationResult> MutateAsync(
        Guid id, Action<Delivery> mutation, CancellationToken cancellationToken)
    {
        var delivery = await deliveries.GetByIdAsync(id, cancellationToken);
        if (delivery is null) return new(DeliveryMutationStatus.NotFound);
        try
        {
            mutation(delivery);
        }
        catch (ArgumentException error)
        {
            return new(DeliveryMutationStatus.Invalid, Error: error.Message);
        }
        catch (InvalidOperationException error)
        {
            return new(DeliveryMutationStatus.Conflict, Error: error.Message);
        }
        return MapSave(await deliveries.UpdateAsync(delivery, cancellationToken), delivery);
    }

    private static DeliveryMutationResult MapSave(
        SaveDeliveryOutcome outcome, Delivery delivery)
        => outcome switch
        {
            SaveDeliveryOutcome.Saved => new(DeliveryMutationStatus.Updated, ToDetails(delivery)),
            SaveDeliveryOutcome.Missing => new(DeliveryMutationStatus.NotFound),
            SaveDeliveryOutcome.Conflict => new(DeliveryMutationStatus.Conflict,
                Error: "The delivery was changed by another operation. Reload and try again."),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome))
        };

    private static DeliveryDetails ToDetails(Delivery delivery)
        => new(
            delivery.Id,
            delivery.RecipientName,
            delivery.PickupAddress,
            delivery.PickupLatitude,
            delivery.PickupLongitude,
            delivery.DropoffAddress,
            delivery.DropoffLatitude,
            delivery.DropoffLongitude,
            delivery.PayloadWeightKg,
            delivery.ExpectedDeliveryAtUtc,
            delivery.DriverId,
            delivery.VehicleId,
            delivery.Status,
            delivery.CreatedAtUtc,
            delivery.AssignedAtUtc,
            delivery.PickedUpAtUtc,
            delivery.DeliveredAtUtc,
            delivery.CancelledAtUtc,
            delivery.Proof is null ? null : new DeliveryProofDetails(
                delivery.Proof.Id,
                delivery.Proof.PhotoReference,
                delivery.Proof.RecipientConfirmation,
                delivery.Proof.Latitude,
                delivery.Proof.Longitude,
                delivery.Proof.CapturedAtUtc));
}
