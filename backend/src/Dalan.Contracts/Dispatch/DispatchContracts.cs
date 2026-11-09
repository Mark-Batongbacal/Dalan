using Dalan.Contracts.FleetOptimization;

namespace Dalan.Contracts.Dispatch;

public sealed record DispatchVehicleStartRequest(
    Guid VehicleId,
    CoordinateRequest? Start);

public sealed record PreviewDispatchRequest(
    IReadOnlyList<DispatchVehicleStartRequest>? Vehicles,
    IReadOnlyList<Guid>? DeliveryIds);

public sealed record DispatchReviewedStopRequest(
    Guid DeliveryId,
    string? Kind);

public sealed record DispatchReviewedRouteRequest(
    Guid VehicleId,
    IReadOnlyList<DispatchReviewedStopRequest>? Stops);

public sealed record DispatchDeliveryVersionRequest(
    Guid DeliveryId,
    byte[]? RowVersion);

public sealed record ConfirmDispatchRequest(
    IReadOnlyList<DispatchVehicleStartRequest>? Vehicles,
    IReadOnlyList<DispatchReviewedRouteRequest>? Routes,
    IReadOnlyList<DispatchDeliveryVersionRequest>? DeliveryVersions);

public sealed record DispatchStopResponse(
    Guid DeliveryId,
    string Kind,
    int Position,
    CoordinateRequest Location);

public sealed record DispatchRouteResponse(
    Guid VehicleId,
    Guid DriverId,
    CoordinateRequest Start,
    IReadOnlyList<DispatchStopResponse> Stops,
    double DistanceMeters,
    double DurationSeconds);

public sealed record DispatchPreviewResponse(
    IReadOnlyList<DispatchRouteResponse> Routes,
    IReadOnlyList<Guid> UnassignedDeliveryIds,
    IReadOnlyList<DispatchDeliveryVersionRequest> DeliveryVersions);

public sealed record DispatchPlanResponse(
    Guid Id,
    DateTimeOffset ConfirmedAtUtc,
    IReadOnlyList<DispatchRouteResponse> Routes);