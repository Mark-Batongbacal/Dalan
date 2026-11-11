namespace Dalan.Infrastructure.Persistence;

public sealed class DriverAccount
{
    public required Guid DriverId { get; init; }
    public required string UserId { get; init; }
}

public sealed class DriverEventReceipt
{
    public required Guid DriverId { get; init; }
    public required Guid EventId { get; init; }
    public required Guid DeliveryId { get; init; }
    public required string PayloadHash { get; init; }
    public byte[]? Photo { get; init; }
}

public sealed class OperationsSnapshot
{
    public required DateOnly DayUtc { get; init; }
    public required string Kind { get; init; }
    public required string Data { get; init; }
}
