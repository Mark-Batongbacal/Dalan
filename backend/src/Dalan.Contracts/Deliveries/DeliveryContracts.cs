using System.ComponentModel.DataAnnotations;

namespace Dalan.Contracts.Deliveries;

public sealed record CreateDeliveryRequest(
    [Required, StringLength(120, MinimumLength = 1)]
    string? RecipientName,

    [Required, StringLength(500, MinimumLength = 1)]
    string? PickupAddress,

    [Required, Range(-90d, 90d)]
    double? PickupLatitude,

    [Required, Range(-180d, 180d)]
    double? PickupLongitude,

    [Required, StringLength(500, MinimumLength = 1)]
    string? DropoffAddress,

    [Required, Range(-90d, 90d)]
    double? DropoffLatitude,

    [Required, Range(-180d, 180d)]
    double? DropoffLongitude,

    decimal? PayloadWeightKg = null,
    DateTimeOffset? ExpectedDeliveryAtUtc = null);

public sealed record AssignDeliveryRequest(
    [Required] Guid? DriverId,
    [Required] Guid? VehicleId);

public sealed record DeliveryProofResponse(
    Guid Id,
    string PhotoReference,
    string RecipientConfirmation,
    double Latitude,
    double Longitude,
    DateTimeOffset CapturedAtUtc);

public sealed record DeliveryResponse(
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
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? AssignedAtUtc,
    DateTimeOffset? PickedUpAtUtc,
    DateTimeOffset? DeliveredAtUtc,
    DateTimeOffset? CancelledAtUtc,
    DeliveryProofResponse? Proof);