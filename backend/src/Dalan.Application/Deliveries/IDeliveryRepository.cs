using Dalan.Domain.Deliveries;

namespace Dalan.Application.Deliveries;

public enum SaveDeliveryOutcome { Saved, Missing, Conflict }

public interface IDeliveryRepository
{
    async Task<IReadOnlyList<Delivery>> ListForPlanningAsync(CancellationToken cancellationToken = default)
        => (await ListAsync(cancellationToken)).Where(x => x.Status is DeliveryStatus.Pending or DeliveryStatus.Assigned).ToArray();
    Task AddAsync(Delivery delivery, CancellationToken cancellationToken = default);
    Task<Delivery?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Delivery>> ListAsync(CancellationToken cancellationToken = default);
    Task<SaveDeliveryOutcome> UpdateAsync(
        Delivery delivery, CancellationToken cancellationToken = default);
    Task<bool> IsInConfirmedPlanAsync(
        Guid deliveryId, CancellationToken cancellationToken = default);
}
