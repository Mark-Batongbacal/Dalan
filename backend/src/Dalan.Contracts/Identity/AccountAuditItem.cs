namespace Dalan.Contracts.Identity;
public sealed record AccountAuditItem(Guid Id, DateTimeOffset OccurredAtUtc, string Action,
    string Outcome, string? ActorUserId, string? TargetUserId, Guid? DriverId,
    string? Reason, string? CorrelationId);
