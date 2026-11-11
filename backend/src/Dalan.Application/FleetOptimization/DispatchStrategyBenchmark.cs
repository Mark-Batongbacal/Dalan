using System.Diagnostics;
using Dalan.Application.Routing;
using Dalan.Contracts.Dispatch;

namespace Dalan.Application.FleetOptimization;

public sealed record DispatchBenchmarkRun(
    double ElapsedMilliseconds, double TotalTravelSeconds, double MakespanSeconds,
    double WorkloadSpreadSeconds, int AssignedDeliveries, int UnassignedDeliveries,
    bool CumulativeCapacityVerified, Guid? NearestDeliveryVehicleId, int MatrixRequests);

public sealed record DispatchBenchmarkResult(
    DispatchStrategy Strategy, bool Compatible, string? Incompatibility,
    IReadOnlyList<DispatchBenchmarkRun> Runs, int PriorityAssignmentSpread);

/// <summary>Explicit offline comparison only. Never registered in the live dispatch workflow.</summary>
public static class DispatchStrategyBenchmark
{
    public static async Task<IReadOnlyList<DispatchBenchmarkResult>> CompareAsync(
        FleetOptimizationRequest batch, TravelMetric?[,] matrix, CancellationToken cancellationToken = default)
    {
        if (batch.Vehicles.Count is < 1 or > 8 || batch.Jobs.Count is < 1 or > 15)
            throw new ArgumentException("Benchmarks use the current 1–8 vehicle and 1–15 delivery limits.");
        var count = batch.Vehicles.Count + 2 * batch.Jobs.Count;
        if (matrix.GetLength(0) != count || matrix.GetLength(1) != count)
            throw new ArgumentException("Benchmark matrix must match the exact ordered vehicle starts and pickup/drop-off pairs.");
        var output = new List<DispatchBenchmarkResult>();
        var stablePriority = batch.Vehicles.Select(x => x.Id).Order().ToArray();
        var nearestJob = Enumerable.Range(0, batch.Jobs.Count).OrderBy(job =>
            matrix[0, batch.Vehicles.Count + job * 2 + 1]?.DurationSeconds ?? double.PositiveInfinity).First();
        foreach (var strategy in Enum.GetValues<DispatchStrategy>())
        {
            var runs = new List<DispatchBenchmarkRun>();
            var counts = stablePriority.ToDictionary(id => id, _ => 0);
            string? incompatible = null;
            for (var turn = 0; turn < batch.Vehicles.Count; turn++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var replay = new ReplayMatrix(matrix);
                var options = (batch.Options ?? new()) with
                {
                    VehiclePriority = stablePriority.Skip(turn).Concat(stablePriority.Take(turn)).ToArray()
                };
                var watch = Stopwatch.StartNew();
                FleetOptimizationResult result;
                try
                {
                    result = await new FleetOptimizer(replay).OptimizeAsync(batch with { Strategy = strategy, Options = options }, cancellationToken);
                }
                catch (ArgumentException error)
                {
                    incompatible = error.Message;
                    break;
                }
                watch.Stop();
                var work = result.Routes.Select(x => x.DurationSeconds + options.ServiceSecondsPerStop * x.Stops.Count).ToArray();
                var nearestVehicle = result.Routes.FirstOrDefault(x => x.Stops.Any(s => s.JobId == batch.Jobs[nearestJob].Id))?.VehicleId;
                if (nearestVehicle is Guid id) counts[id]++;
                runs.Add(new(watch.Elapsed.TotalMilliseconds, result.Routes.Sum(x => x.DurationSeconds), work.Max(), work.Max() - work.Min(),
                    result.Routes.Sum(x => x.Stops.Count) / 2, result.UnassignedJobIds.Count,
                    CapacityVerified(batch, result), nearestVehicle, replay.Calls));
            }
            output.Add(new(strategy, incompatible is null, incompatible, runs,
                counts.Values.Max() - counts.Values.Min()));
        }
        return output;
    }

    private static bool CapacityVerified(FleetOptimizationRequest batch, FleetOptimizationResult result)
    {
        foreach (var route in result.Routes)
        {
            if (route.Stops.Count == 0) continue;
            var vehicle = batch.Vehicles.Single(x => x.Id == route.VehicleId);
            if (vehicle.PayloadCapacityKg is not decimal capacity) return false;
            var load = 0m;
            foreach (var stop in route.Stops)
            {
                if (batch.Jobs.Single(x => x.Id == stop.JobId).PayloadWeightKg is not decimal weight) return false;
                load += stop.Kind == FleetStopKind.Pickup ? weight : -weight;
                if (load < 0 || load > capacity) return false;
            }
        }
        return true;
    }

    private sealed class ReplayMatrix(TravelMetric?[,] matrix) : ITravelMatrixService
    {
        public int Calls { get; private set; }
        public Task<TravelMetric?[,]> GetMatrixAsync(IReadOnlyList<GeoPoint> points, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(matrix);
        }
    }
}
