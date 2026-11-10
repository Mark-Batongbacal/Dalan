using Dalan.Domain.Deliveries;
using Dalan.Domain.Dispatch;
using Dalan.Domain.Drivers;
using Dalan.Domain.Vehicles;

namespace Dalan.Application.Dispatch;

public sealed record DispatchCandidates(
    IReadOnlyList<Delivery> Deliveries,
    IReadOnlyList<Vehicle> Vehicles,
    IReadOnlyList<Driver> Drivers,
    IReadOnlySet<Guid> AlreadyDispatchedDeliveryIds);

public interface IDispatchRepository
{
    Task<DispatchCandidates> ReadCandidatesAsync(
        IReadOnlyList<Guid> deliveryIds,
        IReadOnlyList<Guid> vehicleIds,
        CancellationToken cancellationToken = default);

    Task<bool> ConfirmAsync(
    DispatchPlan plan,
    IReadOnlyList<DispatchDeliveryVersion> expectedVersions,
    CancellationToken cancellationToken = default);

    Task<DispatchPlan?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}