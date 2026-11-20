namespace Dalan.Contracts.Dispatch;

public sealed record WarehousePreference(string Name, double Latitude, double Longitude, DateTimeOffset UpdatedAtUtc);
public sealed record SaveWarehousePreference(string Name, double Latitude, double Longitude);
