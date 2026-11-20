namespace Dalan.Infrastructure.Persistence;
public sealed class DriverGpsException
{
    public Guid Id { get; set; }
    public Guid DriverId { get; set; }
    public Guid DeliveryId { get; set; }
    public DateTimeOffset AssignedAtUtc { get; set; }
    public DateTimeOffset SubmittedAtUtc { get; set; }
    public string Reason { get; set; } = "";
    public string Payload { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public string? ReviewerUserId { get; set; }
    public string? ReviewReason { get; set; }
    public DateTimeOffset? ReviewedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
