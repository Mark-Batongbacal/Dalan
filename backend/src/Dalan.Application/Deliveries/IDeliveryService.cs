using Dalan.Domain.Deliveries;

namespace Dalan.Application.Deliveries;

public sealed record CreateDeliveryCommand(
    string? RecipientName,
    string? PickupAddress,
    double PickupLatitude,
    double PickupLongitude,
    string? DropoffAddress,
    double DropoffLatitude,
    double DropoffLongitude,
    decimal? PayloadWeightKg = null,
    DateTimeOffset? ExpectedDeliveryAtUtc = null);

public sealed record AssignDeliveryCommand(Guid DriverId, Guid VehicleId);

public sealed record CompleteDeliveryCommand(
    Guid DriverId,
    string? PhotoReference,
    string? RecipientConfirmation,
    double Latitude,
    double Longitude);

public sealed record DeliveryProofDetails(
    Guid Id,
    string PhotoReference,
    string RecipientConfirmation,
    double Latitude,
    double Longitude,
    DateTimeOffset CapturedAtUtc);

public sealed record DeliveryDetails(
    Guid Id,
    string RecipientName,
    string PickupAddress,
    double PickupLatitude,
    double PickupLongitude,
    string DropoffAddress,
    double DropoffLatitude,
    double DropoffLongitude,
    decimal? PayloadWeightKg,
    DateTimeOffset? ExpectedDeliveryAtUtc,
    Guid? DriverId,
    Guid? VehicleId,
    DeliveryStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? AssignedAtUtc,
    DateTimeOffset? PickedUpAtUtc,
    DateTimeOffset? DeliveredAtUtc,
    DateTimeOffset? CancelledAtUtc,
    DeliveryProofDetails? Proof);

public enum DeliveryMutationStatus { Created, Updated, NotFound, Invalid, Conflict }

public sealed record DeliveryMutationResult(
    DeliveryMutationStatus Status,
    DeliveryDetails? Delivery = null,
    string? Error = null);

public interface IDeliveryService
{
    Task<DeliveryMutationResult> CreateAsync(
        CreateDeliveryCommand command, CancellationToken cancellationToken = default);
    Task<DeliveryMutationResult> AssignAsync(
        Guid id, AssignDeliveryCommand command, CancellationToken cancellationToken = default);
    Task<DeliveryMutationResult> MarkPickedUpAsync(
        Guid id, Guid driverId, CancellationToken cancellationToken = default);
    Task<DeliveryMutationResult> CompleteAsync(
        Guid id, CompleteDeliveryCommand command, CancellationToken cancellationToken = default);
    Task<DeliveryMutationResult> CancelAsync(
        Guid id, CancellationToken cancellationToken = default);
    Task<DeliveryDetails?> GetByIdAsync(
        Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeliveryDetails>> ListAsync(
        CancellationToken cancellationToken = default);
}
