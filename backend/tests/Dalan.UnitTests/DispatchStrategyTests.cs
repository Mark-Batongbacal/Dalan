using Dalan.Application.Dispatch;
using Dalan.Application.FleetOptimization;
using Dalan.Application.Routing;
using Dalan.Contracts.Dispatch;
using Xunit;

namespace Dalan.UnitTests;

public sealed class DispatchStrategyTests
{
    private static GeoPoint P(double x) => new(x, 0);
    private static FleetJob Job(string id, double destination, Guid? fixedVehicle = null, decimal? weight = 1)
        => new(id, P(0), P(destination), fixedVehicle, weight);

    [Theory]
    [InlineData(DispatchStrategy.WorkloadEquity)]
    [InlineData(DispatchStrategy.EarliestFleetCompletion)]
    public async Task Handling_time_balances_work_without_running_competing_strategies(DispatchStrategy strategy)
    {
        var vehicles = new FleetVehicle[] { new(Guid.NewGuid(), P(0), 20), new(Guid.NewGuid(), P(0), 20) };
        var jobs = Enumerable.Range(1, 4).Select(x => Job(x.ToString(), x)).ToArray();
        var matrix = new RoadMatrix();
        var request = new FleetOptimizationRequest(vehicles, jobs);
        var legacy = await new FleetOptimizer(matrix).OptimizeAsync(request);
        var selected = await new FleetOptimizer(matrix).OptimizeAsync(request with
        {
            Strategy = strategy, Options = new(ServiceSecondsPerStop: 120)
        });
        static double Spread(FleetOptimizationResult result) => result.Routes.Max(x => x.DurationSeconds + 120 * x.Stops.Count)
            - result.Routes.Min(x => x.DurationSeconds + 120 * x.Stops.Count);
        Assert.True(Spread(selected) < Spread(legacy));
        Assert.All(selected.Routes, x => Assert.Equal(4, x.Stops.Count));
        Assert.Empty(selected.UnassignedJobIds);
        Assert.Equal(2, matrix.Calls);
        AssertValid(selected, jobs, vehicles);
    }

    [Fact]
    public async Task Equity_and_makespan_have_distinct_objectives()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var request = new FleetOptimizationRequest(
            [new(first, P(0), 10), new(second, P(10), 10)],
            [new("A", P(0), P(10), first, 1), new("B", P(10), P(12), second, 1), new("C", P(5), P(6), null, 1)]);
        var optimizer = new FleetOptimizer(new RoadMatrix());
        var equity = await optimizer.OptimizeAsync(request with { Strategy = DispatchStrategy.WorkloadEquity });
        var completion = await optimizer.OptimizeAsync(request with { Strategy = DispatchStrategy.EarliestFleetCompletion });

        Assert.Contains(equity.Routes[1].Stops, x => x.JobId == "C");
        Assert.Contains(completion.Routes[0].Stops, x => x.JobId == "C");
        Assert.Equal(10, completion.Routes.Max(x => x.DurationSeconds));
        Assert.True(completion.Routes.Sum(x => x.DurationSeconds) < equity.Routes.Sum(x => x.DurationSeconds));
    }

    [Fact]
    public async Task Warehouse_clusters_use_destination_proximity_and_do_not_cross_transfer()
    {
        var vehicles = new FleetVehicle[] { new(Guid.NewGuid(), P(0), 20), new(Guid.NewGuid(), P(0), 20) };
        var jobs = new[] { Job("A", -10), Job("B", -9), Job("C", 9), Job("D", 10) };
        var result = await new FleetOptimizer(new RoadMatrix(100)).OptimizeAsync(new(vehicles, jobs,
            DispatchStrategy.WarehouseClusterRotation, new(P(0), VehiclePriority: vehicles.Select(x => x.Id).ToArray())));
        Assert.Empty(result.UnassignedJobIds);
        Assert.Equal(["A", "B"], result.Routes[0].Stops.Select(x => x.JobId).Distinct().Order());
        Assert.Equal(["C", "D"], result.Routes[1].Stops.Select(x => x.JobId).Distinct().Order());
        AssertValid(result, jobs, vehicles, warehouse: true);
    }

    [Fact]
    public async Task Complete_link_clusters_prevent_chaining_and_support_more_clusters_than_riders()
    {
        var vehicles = new FleetVehicle[] { new(Guid.NewGuid(), P(0), 20), new(Guid.NewGuid(), P(0), 20) };
        var jobs = new[] { Job("A", 1), Job("B", 6), Job("C", 11), Job("D", 30), Job("E", 31), Job("F", 50) };
        var result = await new FleetOptimizer(new RoadMatrix(100)).OptimizeAsync(new(vehicles, jobs,
            DispatchStrategy.WarehouseClusterRotation, new(P(0), VehiclePriority: vehicles.Select(x => x.Id).ToArray())));
        Assert.Empty(result.UnassignedJobIds);
        Assert.Contains(result.Routes[0].Stops, x => x.JobId == "A");
        Assert.Contains(result.Routes[0].Stops, x => x.JobId == "B");
        Assert.Contains(result.Routes[1].Stops, x => x.JobId == "C");
        Assert.Equal(6, result.Routes.Sum(x => x.Stops.Count) / 2);
        AssertValid(result, jobs, vehicles, warehouse: true);
    }

    [Theory]
    [InlineData(DispatchStrategy.WorkloadEquity)]
    [InlineData(DispatchStrategy.EarliestFleetCompletion)]
    [InlineData(DispatchStrategy.WarehouseClusterRotation)]
    public async Task New_strategies_enforce_cumulative_capacity_and_fixed_assignments(DispatchStrategy strategy)
    {
        var vehicle = new FleetVehicle(Guid.NewGuid(), P(0), 6);
        var jobs = new[] { Job("A", 10, vehicle.Id, 6), Job("B", 11, vehicle.Id, 6) };
        var result = await new FleetOptimizer(new RoadMatrix()).OptimizeAsync(new([vehicle], jobs, strategy, new(P(0))));
        AssertValid(result, jobs, [vehicle], strategy == DispatchStrategy.WarehouseClusterRotation);
        if (strategy == DispatchStrategy.WarehouseClusterRotation) Assert.Single(result.UnassignedJobIds);
        else Assert.Empty(result.UnassignedJobIds); // A second warehouse visit is allowed in general pickup mode.
    }

    [Theory]
    [InlineData(DispatchStrategy.WorkloadEquity)]
    [InlineData(DispatchStrategy.EarliestFleetCompletion)]
    [InlineData(DispatchStrategy.WarehouseClusterRotation)]
    public async Task Unknown_loads_and_unreachable_jobs_are_explicitly_unassigned(DispatchStrategy strategy)
    {
        var vehicle = new FleetVehicle(Guid.NewGuid(), P(0), 10);
        var result = await new FleetOptimizer(new RoadMatrix(unreachable: 80)).OptimizeAsync(new([vehicle],
            [Job("unknown", 1, weight: null), Job("unreachable", 80)], strategy, new(P(0))));
        Assert.Equal(["unknown", "unreachable"], result.UnassignedJobIds);
        Assert.Empty(result.Routes[0].Stops);
    }

    [Fact]
    public async Task Warehouse_validation_precedes_routing_and_general_pickups_still_work()
    {
        var matrix = new RoadMatrix();
        var request = new FleetOptimizationRequest([new(Guid.NewGuid(), P(0), 10)],
            [new("A", P(1), P(2), null, 1)], DispatchStrategy.WarehouseClusterRotation, new(P(0)));
        await Assert.ThrowsAsync<ArgumentException>(() => new FleetOptimizer(matrix).OptimizeAsync(request));
        Assert.Equal(0, matrix.Calls);
        var result = await new FleetOptimizer(matrix).OptimizeAsync(request with { Strategy = DispatchStrategy.MinimumTotalTravelTime });
        Assert.Empty(result.UnassignedJobIds);
    }

    [Fact]
    public async Task Travel_budget_and_repeatability_are_enforced()
    {
        var request = new FleetOptimizationRequest([new(Guid.NewGuid(), P(0), 10)],
            [Job("A", 1), Job("B", 20)], DispatchStrategy.WorkloadEquity,
            new(MaxFleetTravelSeconds: 2));
        var optimizer = new FleetOptimizer(new RoadMatrix());
        var first = await optimizer.OptimizeAsync(request);
        var second = await optimizer.OptimizeAsync(request);
        Assert.Equal(["B"], first.UnassignedJobIds);
        Assert.True(first.Routes.Sum(x => x.DurationSeconds) <= 2);
        Assert.Equal(first.Routes.SelectMany(x => x.Stops), second.Routes.SelectMany(x => x.Stops));
    }

    [Fact]
    public void Stable_rotation_skips_absent_riders_and_inserts_newcomers_without_resetting()
    {
        var first = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var second = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var third = Guid.Parse("00000000-0000-0000-0000-000000000003");
        Assert.Equal([second, third, first], new DispatchRotation(1, first).OrderRiders([third, first, second]));
        Assert.Equal([third, first], new DispatchRotation(2, second).OrderRiders([first, third]));
        Assert.Equal([first, second, third], new DispatchRotation(3, third).OrderRiders([second, third, first]));
    }

    [Theory]
    [InlineData(2, 4, true, 60, DispatchStrategy.EarliestFleetCompletion)]
    [InlineData(2, 4, false, 60, DispatchStrategy.MinimumTotalTravelTime)]
    [InlineData(2, 4, true, 0, DispatchStrategy.MinimumTotalTravelTime)]
    [InlineData(3, 5, true, 120, DispatchStrategy.MinimumTotalTravelTime)]
    public void Advice_uses_only_metadata_and_discloses_its_basis(int vehicles, int jobs, bool known, double service, DispatchStrategy expected)
    {
        var advice = DispatchStrategyAdvice.Suggest(vehicles, jobs, known, service);
        Assert.Equal(expected, advice.Strategy);
        Assert.NotEmpty(advice.Basis);
        Assert.NotEmpty(advice.Confidence);
    }

    [Fact]
    public async Task Offline_benchmark_replays_identical_matrices_and_measures_rotation()
    {
        var vehicles = new FleetVehicle[] { new(Guid.NewGuid(), P(0), 20), new(Guid.NewGuid(), P(0), 20) };
        var jobs = new[] { Job("A", 1), Job("B", 2), Job("C", 20), Job("D", 21) };
        var request = new FleetOptimizationRequest(vehicles, jobs, Options: new(P(0), 120));
        var points = vehicles.Select(x => x.Start).Concat(jobs.SelectMany(x => new[] { x.Pickup, x.Dropoff })).ToArray();
        var matrix = await new RoadMatrix(100).GetMatrixAsync(points);
        var results = await DispatchStrategyBenchmark.CompareAsync(request, matrix);
        Assert.Equal(4, results.Count);
        Assert.All(results, result =>
        {
            Assert.True(result.Compatible);
            Assert.Equal(2, result.Runs.Count);
            Assert.All(result.Runs, run => { Assert.Equal(1, run.MatrixRequests); Assert.Equal(4, run.AssignedDeliveries); });
        });
        Assert.Equal(0, results.Single(x => x.Strategy == DispatchStrategy.WarehouseClusterRotation).PriorityAssignmentSpread);
        Assert.Equal(2, results.Single(x => x.Strategy == DispatchStrategy.MinimumTotalTravelTime).PriorityAssignmentSpread);
    }

    [Fact]
    public async Task Fixed_warehouse_assignments_are_reserved_before_flexible_clusters()
    {
        var first = new FleetVehicle(Guid.NewGuid(), P(0), 2);
        var second = new FleetVehicle(Guid.NewGuid(), P(0), 2);
        var jobs = new[] { Job("A-flexible", 1, weight: 2), Job("Z-fixed", 20, first.Id, 2) };
        var result = await new FleetOptimizer(new RoadMatrix(100)).OptimizeAsync(new([first, second], jobs,
            DispatchStrategy.WarehouseClusterRotation, new(P(0), VehiclePriority: [first.Id, second.Id])));
        Assert.Empty(result.UnassignedJobIds);
        Assert.Contains(result.Routes[0].Stops, stop => stop.JobId == "Z-fixed");
        Assert.Contains(result.Routes[1].Stops, stop => stop.JobId == "A-flexible");
    }

    private static void AssertValid(FleetOptimizationResult result, IReadOnlyList<FleetJob> jobs,
        IReadOnlyList<FleetVehicle> vehicles, bool warehouse = false)
    {
        var seen = new HashSet<string>();
        foreach (var route in result.Routes)
        {
            var load = 0m; var departed = false;
            foreach (var stop in route.Stops)
            {
                var job = jobs.Single(x => x.Id == stop.JobId);
                if (job.AssignedVehicleId is Guid fixedId) Assert.Equal(fixedId, route.VehicleId);
                if (stop.Kind == FleetStopKind.Pickup)
                {
                    Assert.True(seen.Add(stop.JobId));
                    if (warehouse) Assert.False(departed);
                    load += job.PayloadWeightKg!.Value;
                }
                else
                {
                    Assert.Contains(stop.JobId, seen);
                    departed = true;
                    load -= job.PayloadWeightKg!.Value;
                }
                Assert.InRange(load, 0, vehicles.Single(x => x.Id == route.VehicleId).PayloadCapacityKg!.Value);
            }
            Assert.Equal(0, load);
        }
        Assert.Equal(jobs.Count, seen.Count + result.UnassignedJobIds.Count);
    }

    private sealed class RoadMatrix(double scale = 1, double? unreachable = null) : ITravelMatrixService
    {
        public int Calls { get; private set; }
        public Task<TravelMetric?[,]> GetMatrixAsync(IReadOnlyList<GeoPoint> points, CancellationToken cancellationToken = default)
        {
            Calls++;
            var matrix = new TravelMetric?[points.Count, points.Count];
            for (var from = 0; from < points.Count; from++)
            for (var to = 0; to < points.Count; to++)
                if (from == to || points[from].Latitude != unreachable && points[to].Latitude != unreachable)
                {
                    var time = scale * Math.Abs(points[from].Latitude - points[to].Latitude);
                    matrix[from, to] = new(time * 10, time);
                }
            return Task.FromResult(matrix);
        }
    }
}
