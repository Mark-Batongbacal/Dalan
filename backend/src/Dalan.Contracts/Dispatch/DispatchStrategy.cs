using System.Text.Json.Serialization;
using System.Text.Json;

namespace Dalan.Contracts.Dispatch;

[JsonConverter(typeof(DispatchStrategyJsonConverter))]
public enum DispatchStrategy
{
    MinimumTotalTravelTime,
    WorkloadEquity,
    WarehouseClusterRotation,
    EarliestFleetCompletion
}

public sealed class DispatchStrategyJsonConverter : JsonConverter<DispatchStrategy>
{
    public override DispatchStrategy Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && reader.GetString() is string name
            && Enum.TryParse<DispatchStrategy>(name, out var strategy)
            && Enum.IsDefined(strategy) && name == strategy.ToString()) return strategy;
        throw new JsonException("Select a supported dispatch strategy by name.");
    }

    public override void Write(Utf8JsonWriter writer, DispatchStrategy value, JsonSerializerOptions options)
    {
        if (!Enum.IsDefined(value)) throw new JsonException("Unsupported dispatch strategy.");
        writer.WriteStringValue(value.ToString());
    }
}

public sealed record DispatchStrategySettings(
    Dalan.Contracts.FleetOptimization.CoordinateRequest? Warehouse = null,
    double ServiceSecondsPerStop = 0,
    double? MaxFleetTravelSeconds = null);

public sealed record DispatchRouteMetrics(
    Guid VehicleId, int DeliveryCount, double TravelSeconds, double EstimatedWorkloadSeconds);

public sealed record DispatchPlanMetrics(
    double TotalTravelSeconds, double LongestWorkloadSeconds, double WorkloadSpreadSeconds,
    double ServiceSecondsPerStop, IReadOnlyList<DispatchRouteMetrics> Routes,
    IReadOnlyList<string> Warnings);
