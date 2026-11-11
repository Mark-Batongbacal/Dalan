using Dalan.Application.Routing;
using Dalan.Domain.Dispatch;
using Dalan.Contracts.Dispatch;

namespace Dalan.Application.Dispatch;

public sealed record DispatchVehicleStart(Guid VehicleId, GeoPoint Start);

public sealed record DispatchPreviewRequest(
    IReadOnlyList<DispatchVehicleStart> Vehicles,
    IReadOnlyList<Guid> DeliveryIds,
    DispatchStrategy Strategy = DispatchStrategy.MinimumTotalTravelTime,
    DispatchStrategySettings? Settings = null);

public sealed record DispatchDeliveryVersion(Guid DeliveryId, byte[] RowVersion);

public sealed record DispatchPreviewStop(
    Guid DeliveryId,
    DispatchStopKind Kind,
    GeoPoint Location);

public sealed record DispatchPreviewRoute(
    Guid VehicleId,
    Guid DriverId,
    GeoPoint Start,
    IReadOnlyList<DispatchPreviewStop> Stops,
    double DistanceMeters,
    double DurationSeconds);

public sealed record DispatchPreviewResult(
    IReadOnlyList<DispatchPreviewRoute> Routes,
    IReadOnlyList<Guid> UnassignedDeliveryIds,
    IReadOnlyList<DispatchDeliveryVersion> DeliveryVersions,
    DispatchStrategy Strategy = DispatchStrategy.MinimumTotalTravelTime,
    DispatchPlanMetrics? Metrics = null,
    long? RotationVersion = null);
public sealed record DispatchReviewedStop(
    Guid DeliveryId,
    DispatchStopKind Kind);

public sealed record DispatchReviewedRoute(
    Guid VehicleId,
    IReadOnlyList<DispatchReviewedStop> Stops);

public sealed record DispatchConfirmRequest(
    IReadOnlyList<DispatchVehicleStart> Vehicles,
    IReadOnlyList<DispatchReviewedRoute> Routes,
    IReadOnlyList<DispatchDeliveryVersion> DeliveryVersions,
    DispatchStrategy Strategy = DispatchStrategy.MinimumTotalTravelTime,
    DispatchStrategySettings? Settings = null,
    long? RotationVersion = null);

public interface IDispatchService
{
    Task<DispatchPreviewResult> PreviewAsync(
        DispatchPreviewRequest request,
        CancellationToken cancellationToken = default);

    Task<DispatchPlan?> ConfirmAsync(
    DispatchConfirmRequest request,
    CancellationToken cancellationToken = default);


}
