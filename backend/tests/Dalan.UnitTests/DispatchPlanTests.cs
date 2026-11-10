using Dalan.Domain.Dispatch;
using Xunit;

namespace Dalan.UnitTests;

public sealed class DispatchPlanTests
{
    [Fact]
    public void Route_accepts_interleaved_deliveries_with_pickup_precedence()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var route = DispatchRoute.Create(
            Guid.NewGuid(), Guid.NewGuid(),
            14.6, 121.0, 1200, 300,
            [
                Stop(first, DispatchStopKind.Pickup, 0),
                Stop(second, DispatchStopKind.Pickup, 1),
                Stop(first, DispatchStopKind.Dropoff, 2),
                Stop(second, DispatchStopKind.Dropoff, 3)
            ]);

        var plan = DispatchPlan.Create([route], DateTimeOffset.UtcNow);

        Assert.Equal(4, plan.Routes[0].Stops.Count);
    }

    [Fact]
    public void Route_rejects_duplicate_pickup()
    {
        var deliveryId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => DispatchRoute.Create(
            Guid.NewGuid(), Guid.NewGuid(),
            14.6, 121.0, 1200, 300,
            [
                Stop(deliveryId, DispatchStopKind.Pickup, 0),
                Stop(deliveryId, DispatchStopKind.Pickup, 1)
            ]));
    }

    [Fact]
    public void Plan_rejects_delivery_on_two_routes()
    {
        var deliveryId = Guid.NewGuid();
        var first = Route(deliveryId);
        var second = Route(deliveryId);

        Assert.Throws<ArgumentException>(() =>
            DispatchPlan.Create([first, second], DateTimeOffset.UtcNow));
    }

    private static DispatchRoute Route(Guid deliveryId)
        => DispatchRoute.Create(
            Guid.NewGuid(), Guid.NewGuid(),
            14.6, 121.0, 1200, 300,
            [
                Stop(deliveryId, DispatchStopKind.Pickup, 0),
                Stop(deliveryId, DispatchStopKind.Dropoff, 1)
            ]);

    private static DispatchStop Stop(
        Guid deliveryId, DispatchStopKind kind, int position)
        => DispatchStop.Create(deliveryId, kind, position, 14.6, 121.0);
}