using Dalan.Application.Routing;
using Dalan.Domain.Dispatch;

namespace Dalan.Application.Dispatch;

public sealed record DispatchVehicleStart(Guid VehicleId, GeoPoint Start);

public sealed record DispatchPreviewRequest(
    IReadOnlyList<DispatchVehicleStart> Vehicles,
    IReadOnlyList<Guid> DeliveryIds);

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
    IReadOnlyList<DispatchDeliveryVersion> DeliveryVersions);
public sealed record DispatchReviewedStop(
    Guid DeliveryId,
    DispatchStopKind Kind);

public sealed record DispatchReviewedRoute(
    Guid VehicleId,
    IReadOnlyList<DispatchReviewedStop> Stops);

public sealed record DispatchConfirmRequest(
    IReadOnlyList<DispatchVehicleStart> Vehicles,
    IReadOnlyList<DispatchReviewedRoute> Routes,
    IReadOnlyList<DispatchDeliveryVersion> DeliveryVersions);

public interface IDispatchService
{
    Task<DispatchPreviewResult> PreviewAsync(
        DispatchPreviewRequest request,
        CancellationToken cancellationToken = default);

    Task<DispatchPlan?> ConfirmAsync(
    DispatchConfirmRequest request,
    CancellationToken cancellationToken = default);


}