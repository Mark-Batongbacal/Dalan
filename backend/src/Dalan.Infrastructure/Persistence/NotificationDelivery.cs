namespace Dalan.Infrastructure.Persistence;

public sealed class NotificationDelivery
{
    public required string Id { get; init; }
    public required DateOnly DayUtc { get; init; }
    public required string Kind { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required string From { get; init; }
    public required string ToJson { get; init; }
    public required string Subject { get; init; }
    public required string Text { get; set; }
    public Guid Claim { get; set; }
    public DateTimeOffset? LeaseUntilUtc { get; set; }
    public DateTimeOffset? NextAttemptAtUtc { get; set; }
    public DateTimeOffset? AcceptedAtUtc { get; set; }
    public string? ProviderId { get; set; }
    public int Attempts { get; set; }
    public bool NeedsReview { get; set; }
}
