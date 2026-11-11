using Dalan.Contracts.Dispatch;

namespace Dalan.Application.Dispatch;

public sealed record DispatchStrategyAdvice(DispatchStrategy Strategy, string Basis, string Confidence)
{
    // Metadata only: this path has no optimizer or routing-provider dependency.
    public static DispatchStrategyAdvice Suggest(int vehicleCount, int deliveryCount,
        bool knownPayloadsAndCapacities, double serviceSecondsPerStop)
    {
        if (vehicleCount >= 2 && deliveryCount >= 2 * vehicleCount
            && knownPayloadsAndCapacities && double.IsFinite(serviceSecondsPerStop) && serviceSecondsPerStop >= 60)
            return new(DispatchStrategy.EarliestFleetCompletion,
                "At least two deliveries per vehicle and at least 60 seconds of handling per stop. Synthetic handling and clustered fixtures reduced the longest workload with this objective, with more total travel. No road matrix has been requested for this suggestion.",
                "Low: benchmark-informed heuristic; this batch has not been scored.");
        return new(DispatchStrategy.MinimumTotalTravelTime,
            "Keep the existing travel-time default policy. The available metadata does not meet the benchmark-backed handling rule; road geography, existing work and feasibility have not been evaluated.",
            "Default policy, with no claim that it is best for this batch.");
    }
}
