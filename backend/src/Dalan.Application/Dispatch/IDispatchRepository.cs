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
    Task<DispatchRotation> ReadRotationAsync(string warehouseKey, CancellationToken cancellationToken = default);
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

public sealed record DispatchRotation(long Version, Guid? LastFirstDriverId)
{
    // Stable identity order: skip absent riders; newcomers join by ID. A removed cursor
    // remains an ordering boundary so absence never resets the priority to the first rider.
    public Guid[] OrderRiders(IEnumerable<Guid> available)
    {
        var ordered = available.Distinct().Order().ToArray();
        if (LastFirstDriverId is not Guid last) return ordered;
        return ordered.Where(id => id.CompareTo(last) > 0)
            .Concat(ordered.Where(id => id.CompareTo(last) <= 0)).ToArray();
    }
}
