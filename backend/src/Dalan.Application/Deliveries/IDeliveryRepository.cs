using Dalan.Domain.Deliveries;

namespace Dalan.Application.Deliveries;

public enum SaveDeliveryOutcome { Saved, Missing, Conflict }

public interface IDeliveryRepository
{
    Task AddAsync(Delivery delivery, CancellationToken cancellationToken = default);
    Task<Delivery?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Delivery>> ListAsync(CancellationToken cancellationToken = default);
    Task<SaveDeliveryOutcome> UpdateAsync(
        Delivery delivery, CancellationToken cancellationToken = default);
}
