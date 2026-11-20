using System.Text.Json;
using Dalan.Application.DriverExecution;
using Dalan.Application.Routing;
using Dalan.Contracts.DriverExecution;
using Xunit;

namespace Dalan.UnitTests;

public sealed class DriverNavigationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid RouteId = Guid.NewGuid();
    private static DriverTask Delivery(string status, int pickup, int dropoff) => new(Guid.NewGuid(), "Demo", "Depot", "Destination",
        status, Now.AddHours(-1), RouteId, pickup, dropoff, 15 + pickup * .001, 120, 15 + dropoff * .001, 120);

    [Fact]
    public async Task Full_navigation_connects_current_position_to_every_remaining_stop_in_confirmed_order()
    {
        var onboard = Delivery("PickedUp", 1, 3);
        var awaiting = Delivery("Assigned", 2, 4);
        var provider = new Routing();
        var service = new DriverNavigationService(new Tasks([onboard, awaiting]), [provider], new Clock());
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var request = JsonSerializer.Deserialize<DriverNavigationRequest>(
            """{"origin":{"latitude":15,"longitude":120},"capturedAtUtc":"2026-11-20T10:00:00Z","accuracyMeters":8,"includeRemainingStops":true}""", options)!;
        var result = await service.Navigate(Guid.NewGuid(), request, default);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result, options));
        Assert.True(json.RootElement.TryGetProperty("stops", out var stops), "Navigation must retain every remaining stop, not only the next destination.");
        Assert.Equal(3, stops.GetArrayLength());
        Assert.Equal(new[] { awaiting.Id, onboard.Id, awaiting.Id }, stops.EnumerateArray().Select(x => x.GetProperty("deliveryId").GetGuid()));
        Assert.Equal(new[] { "Pickup", "Drop-off", "Drop-off" }, stops.EnumerateArray().Select(x => x.GetProperty("stopKind").GetString()));
        Assert.Equal(new GeoPoint(15, 120), provider.Legs[0].From);
        Assert.Equal(provider.Legs[0].To, provider.Legs[1].From);
        Assert.Equal(provider.Legs[1].To, provider.Legs[2].From);
        Assert.Equal(3, provider.Legs.Count);
    }

    [Fact]
    public async Task Existing_clients_still_request_only_the_next_leg()
    {
        var provider = new Routing();
        var service = new DriverNavigationService(new Tasks([Delivery("Assigned", 1, 2)]), [provider], new Clock());
        Assert.NotNull(await service.Navigate(Guid.NewGuid(), new(new(15, 120), Now, 8), default));
        Assert.Single(provider.Legs);
    }

    [Fact]
    public async Task Stale_position_is_rejected_before_routing()
    {
        var provider = new Routing();
        var service = new DriverNavigationService(new Tasks([Delivery("Assigned", 1, 2)]), [provider], new Clock());
        await Assert.ThrowsAsync<ArgumentException>(() => service.Navigate(Guid.NewGuid(), new(new(15, 120), Now.AddMinutes(-3), 8), default));
        Assert.Empty(provider.Legs);
    }

    [Fact]
    public async Task Shared_warehouse_pickups_remain_visible_without_requesting_a_zero_length_road_route()
    {
        var provider = new Routing();
        var service = new DriverNavigationService(new Tasks([Delivery("Assigned", 1, 3), Delivery("Assigned", 2, 4) with { PickupLatitude = 15.001 }]), [provider], new Clock());
        var result = await service.Navigate(Guid.NewGuid(), new(new(15, 120), Now, 8, true), default);
        Assert.Equal(4, result!.Stops!.Count);
        Assert.Equal(3, provider.Legs.Count);
        Assert.Equal(0, result.Stops[1].Route.DistanceMeters);
    }

    [Fact]
    public async Task No_active_deliveries_returns_no_route()
    {
        var provider = new Routing();
        var service = new DriverNavigationService(new Tasks([Delivery("Delivered", 1, 2)]), [provider], new Clock());
        Assert.Null(await service.Navigate(Guid.NewGuid(), new(new(15, 120), Now, 8, true), default));
        Assert.Empty(provider.Legs);
    }

    [Theory]
    [InlineData("null", 0)]
    [InlineData("{\"latitude\":15,\"longitude\":120}", 100)]
    public async Task Preview_can_use_assigned_stops_or_a_fresh_approximate_origin(string origin, int accuracy)
    {
        var provider = new Routing();
        var delivery = Delivery("Assigned", 1, 2);
        var service = new DriverNavigationService(new Tasks([delivery]), [provider], new Clock());
        var request = JsonSerializer.Deserialize<DriverNavigationRequest>(
            $$"""{"origin":{{origin}},"capturedAtUtc":"2026-11-20T10:00:00Z","accuracyMeters":{{accuracy}},"includeRemainingStops":true,"previewOnly":true}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var result = await service.Navigate(Guid.NewGuid(), request, default);
        Assert.Equal(2, result!.Stops!.Count);
        Assert.Equal(origin == "null" ? new GeoPoint(delivery.PickupLatitude, delivery.PickupLongitude) : new GeoPoint(15, 120), provider.Legs[0].From);
        Assert.Equal(0, provider.DirectionsRequests);
    }

    [Fact]
    public async Task Guidance_still_rejects_an_imprecise_origin()
    {
        var provider = new Routing();
        var service = new DriverNavigationService(new Tasks([Delivery("Assigned", 1, 2)]), [provider], new Clock());
        await Assert.ThrowsAsync<ArgumentException>(() => service.Navigate(Guid.NewGuid(), new(new(15, 120), Now, 100, true), default));
        Assert.Empty(provider.Legs);
    }

    [Theory]
    [InlineData(1001, 0)]
    [InlineData(100, -3)]
    public async Task Preview_rejects_an_unusable_supplied_position(int accuracy, int minutes)
    {
        var provider = new Routing();
        var service = new DriverNavigationService(new Tasks([Delivery("Assigned", 1, 2)]), [provider], new Clock());
        await Assert.ThrowsAsync<ArgumentException>(() => service.Navigate(Guid.NewGuid(), new(new(15, 120), Now.AddMinutes(minutes), accuracy, true, true), default));
        Assert.Empty(provider.Legs);
    }

    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Routing : IRoutingService
    {
        public List<(GeoPoint From, GeoPoint To)> Legs { get; } = [];
        public int DirectionsRequests { get; private set; }
        public Task<RoadRoute?> GetDirectionsAsync(GeoPoint origin, GeoPoint destination, CancellationToken token = default)
        { DirectionsRequests++; return GetRouteAsync(origin, destination, token); }
        public Task<RoadRoute?> GetRouteAsync(GeoPoint origin, GeoPoint destination, CancellationToken token = default)
        { Legs.Add((origin, destination)); return Task.FromResult<RoadRoute?>(new(100, 10, "test-polyline")); }
    }
    private sealed class Tasks(IReadOnlyList<DriverTask> tasks) : IDriverTasks
    {
        public Task<IReadOnlyList<DriverTask>> ListAsync(Guid driverId, CancellationToken token) => Task.FromResult(tasks);
        public Task<Guid?> FindDriverAsync(string userId, CancellationToken token) => throw new NotSupportedException();
        public Task<DriverRouteStops?> GetRouteStopsAsync(Guid driverId, Guid deliveryId, CancellationToken token) => throw new NotSupportedException();
        public Task<DriverEventResult> ApplyAsync(Guid driverId, Guid deliveryId, DriverTaskEvent request, CancellationToken token) => throw new NotSupportedException();
        public Task<byte[]?> GetProofPhotoAsync(Guid deliveryId, CancellationToken token) => throw new NotSupportedException();
    }
}
