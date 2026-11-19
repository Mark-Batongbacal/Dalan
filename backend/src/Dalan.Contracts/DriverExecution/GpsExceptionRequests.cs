using System.ComponentModel.DataAnnotations;
namespace Dalan.Contracts.DriverExecution;
public sealed record GpsExceptionRequest([Required] DriverTaskEvent Proof, [Required, StringLength(500, MinimumLength = 8)] string Reason);
public sealed record GpsExceptionReview(bool Approve, [Required, StringLength(500, MinimumLength = 8)] string Reason);
public sealed record GpsExceptionItem(Guid Id, Guid DriverId, Guid DeliveryId, DateTimeOffset SubmittedAtUtc,
    string Reason, string Status, string? ReviewReason, string? ReviewerUserId, DateTimeOffset? ReviewedAtUtc,
    double? Latitude, double? Longitude, double? AccuracyMeters, DateTimeOffset? FixAtUtc,
    string Recipient, double DestinationLatitude, double DestinationLongitude);
