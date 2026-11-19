namespace Dalan.Infrastructure.Persistence;

/// <summary>
/// Append-only account security event. Store identifiers rather than user
/// supplied emails, credentials, request bodies or one-time links.
/// Database users should receive SELECT/INSERT only on this table; maintenance
/// deletion is a separate restricted retention operation.
/// </summary>
public sealed class AccountAccessAudit
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAtUtc { get; init; }
    public required string Action { get; init; }
    public required string Outcome { get; init; }
    public string? ActorUserId { get; init; }
    public string? TargetUserId { get; init; }
    public Guid? DriverId { get; init; }
    public string? Reason { get; init; }
    public string? CorrelationId { get; init; }
}
