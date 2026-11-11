namespace Dalan.Infrastructure.Persistence;

public sealed class DispatchRotationState
{
    public string WarehouseKey { get; set; } = string.Empty;
    public long Version { get; set; }
    public Guid? LastFirstDriverId { get; set; }
}
