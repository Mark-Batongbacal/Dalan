using Dalan.Domain.Deliveries;

namespace Dalan.Application.Deliveries;

public sealed record DeliveryBatchResult(bool Created, IReadOnlyList<Guid> DeliveryIds, string? Error = null);
public interface IDeliveryBatchRepository
{
    Task AddRangeAsync(IReadOnlyList<Delivery> deliveries, CancellationToken token = default);
}
public interface IDeliveryBatchService
{
    Task<DeliveryBatchResult> CreateAsync(IReadOnlyList<CreateDeliveryCommand> commands, CancellationToken token = default);
}
public sealed class DeliveryBatchService(IDeliveryBatchRepository repository, TimeProvider clock) : IDeliveryBatchService
{
    public async Task<DeliveryBatchResult> CreateAsync(IReadOnlyList<CreateDeliveryCommand> commands, CancellationToken token = default)
    {
        if (commands.Count is < 1 or > 15) return new(false, [], "Create between 1 and 15 deliveries per batch.");
        var items = new List<Delivery>();
        foreach (var command in commands)
        {
            try { items.Add(Delivery.Create(command.RecipientName, command.PickupAddress, command.PickupLatitude, command.PickupLongitude,
                command.DropoffAddress, command.DropoffLatitude, command.DropoffLongitude, clock.GetUtcNow(), command.PayloadWeightKg, command.ExpectedDeliveryAtUtc)); }
            catch (ArgumentException error) { return new(false, [], $"Delivery {items.Count + 1}: {error.Message}"); }
        }
        await repository.AddRangeAsync(items, token);
        return new(true, items.Select(x => x.Id).ToArray());
    }
}
