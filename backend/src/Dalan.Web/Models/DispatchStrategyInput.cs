using Dalan.Contracts.Dispatch;
using Dalan.Contracts.FleetOptimization;

namespace Dalan.Web.Models;

public sealed class DispatchStrategyInput
{
    public double ServiceSecondsPerStop { get; set; }
    public double? MaxFleetTravelSeconds { get; set; }
    public double? WarehouseLatitude { get; set; }
    public double? WarehouseLongitude { get; set; }

    public DispatchStrategySettings ToContract() => new(
        WarehouseLatitude is double latitude && WarehouseLongitude is double longitude
            ? new CoordinateRequest(latitude, longitude) : null,
        ServiceSecondsPerStop, MaxFleetTravelSeconds);

    public static string Label(DispatchStrategy strategy) => strategy switch
    {
        DispatchStrategy.MinimumTotalTravelTime => "Minimum total fleet travel time (default policy)",
        DispatchStrategy.WorkloadEquity => "Workload equity",
        DispatchStrategy.WarehouseClusterRotation => "Warehouse proximity clusters with rider rotation",
        DispatchStrategy.EarliestFleetCompletion => "Earliest fleet completion",
        _ => "Unknown strategy"
    };
}
