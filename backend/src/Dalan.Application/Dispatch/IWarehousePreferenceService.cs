using Dalan.Contracts.Dispatch;

namespace Dalan.Application.Dispatch;

public interface IWarehousePreferenceRepository
{
    Task<WarehousePreference?> GetAsync(string userId, CancellationToken token = default);
    Task SaveAsync(string userId, WarehousePreference preference, CancellationToken token = default);
}

public interface IWarehousePreferenceService
{
    Task<WarehousePreference?> GetAsync(string userId, CancellationToken token = default);
    Task SaveAsync(string userId, SaveWarehousePreference request, CancellationToken token = default);
}

public sealed class WarehousePreferenceService(IWarehousePreferenceRepository repository, TimeProvider clock) : IWarehousePreferenceService
{
    public Task<WarehousePreference?> GetAsync(string userId, CancellationToken token = default) => repository.GetAsync(userId, token);
    public Task SaveAsync(string userId, SaveWarehousePreference request, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200
            || !double.IsFinite(request.Latitude) || Math.Abs(request.Latitude) > 90
            || !double.IsFinite(request.Longitude) || Math.Abs(request.Longitude) > 180)
            throw new ArgumentException("Enter a warehouse name and confirm a valid map location.");
        return repository.SaveAsync(userId, new(request.Name.Trim(), request.Latitude, request.Longitude, clock.GetUtcNow()), token);
    }
}
