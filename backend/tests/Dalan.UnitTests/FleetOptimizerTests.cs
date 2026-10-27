using Dalan.Application.FleetOptimization;
using Dalan.Application.Routing;
using Xunit;

namespace Dalan.UnitTests;

public sealed class FleetOptimizerTests
{
    [Fact]
    public async Task Assigns_each_job_to_the_nearer_vehicle_and_preserves_precedence()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var request = new FleetOptimizationRequest(
            [new(first, Point(0)), new(second, Point(10))],
            [new("A", Point(1), Point(2)), new("B", Point(9), Point(8))]);

        var result = await new FleetOptimizer(new LineMatrix()).OptimizeAsync(request);

        Assert.Empty(result.UnassignedJobIds);
        Assert.Equal(["A", "A"], result.Routes[0].Stops.Select(stop => stop.JobId));
        Assert.Equal(["B", "B"], result.Routes[1].Stops.Select(stop => stop.JobId));
        Assert.Equal(FleetStopKind.Pickup, result.Routes[0].Stops[0].Kind);
        Assert.Equal(FleetStopKind.Dropoff, result.Routes[0].Stops[1].Kind);
        Assert.Equal(2, result.Routes[0].DurationSeconds);
    }

    [Fact]
    public async Task Reorders_fixed_jobs_within_their_vehicle()
    {
        var vehicle = Guid.NewGuid();
        var request = new FleetOptimizationRequest(
            [new(vehicle, Point(0))],
            [new("A", Point(8), Point(9), vehicle),
             new("B", Point(1), Point(2), vehicle)]);

        var result = await new FleetOptimizer(new LineMatrix()).OptimizeAsync(request);

        Assert.Empty(result.UnassignedJobIds);
        Assert.Equal(["B", "B", "A", "A"],
            result.Routes[0].Stops.Select(stop => stop.JobId));
        Assert.Equal(9, result.Routes[0].DurationSeconds);
    }

    [Fact]
    public async Task Unreachable_job_is_reported_without_a_broken_route()
    {
        var request = new FleetOptimizationRequest(
            [new(Guid.NewGuid(), Point(0))], [new("A", Point(1), Point(2))]);

        var result = await new FleetOptimizer(new LineMatrix(unreachable: true)).OptimizeAsync(request);

        Assert.Equal(["A"], result.UnassignedJobIds);
        Assert.Empty(result.Routes[0].Stops);
    }

    [Fact]
    public async Task Unknown_fixed_vehicle_is_rejected_before_provider_call()
    {
        var request = new FleetOptimizationRequest(
            [new(Guid.NewGuid(), Point(0))],
            [new("A", Point(1), Point(2), Guid.NewGuid())]);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new FleetOptimizer(new LineMatrix()).OptimizeAsync(request));
    }

    private static GeoPoint Point(double latitude) => new(latitude, 0);

    private sealed class LineMatrix(bool unreachable = false) : ITravelMatrixService
    {
        public Task<TravelMetric?[,]> GetMatrixAsync(
            IReadOnlyList<GeoPoint> points, CancellationToken cancellationToken = default)
        {
            var result = new TravelMetric?[points.Count, points.Count];
            for (var from = 0; from < points.Count; from++)
            for (var to = 0; to < points.Count; to++)
            {
                if (unreachable && from != to) continue;
                var distance = Math.Abs(points[from].Latitude - points[to].Latitude);
                result[from, to] = new TravelMetric(distance * 1000, distance);
            }
            return Task.FromResult(result);
        }
    }
}
