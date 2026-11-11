using System.ComponentModel.DataAnnotations;

namespace Dalan.Contracts.DriverExecution;

public sealed record DriverTask(Guid Id, string RecipientName, string PickupAddress,
    string DropoffAddress, string Status, DateTimeOffset AssignedAtUtc, Guid? RouteId,
    int? PickupPosition, int? DropoffPosition,
    double PickupLatitude, double PickupLongitude, double DropoffLatitude, double DropoffLongitude);

public sealed record DriverTaskEvent(
    Guid EventId,
    DateTimeOffset AssignedAtUtc,
    DateTimeOffset CapturedAtUtc,
    [Required, RegularExpression("Pickup|Deliver")] string Kind,
    [Range(-90d, 90d)] double? Latitude,
    [Range(-180d, 180d)] double? Longitude,
    [StringLength(120)] string? RecipientConfirmation,
    [MaxLength(512_000)] byte[]? Photo);
