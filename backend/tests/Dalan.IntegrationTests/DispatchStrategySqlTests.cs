using Dalan.Application.Dispatch;
using Dalan.Application.FleetOptimization;
using Dalan.Application.Routing;
using Dalan.Contracts.Dispatch;
using Dalan.Contracts.FleetOptimization;
using Dalan.Domain.Deliveries;
using Dalan.Domain.Drivers;
using Dalan.Domain.Vehicles;
using Dalan.Infrastructure.Persistence;
using Dalan.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Dalan.IntegrationTests;

[Collection("SQL Server")]
public sealed class DispatchStrategySqlTests(SqlServerFixture fixture)
{
    private static int nextWarehouse;

    [Theory]
    [InlineData(DispatchStrategy.MinimumTotalTravelTime)]
    [InlineData(DispatchStrategy.WorkloadEquity)]
    [InlineData(DispatchStrategy.WarehouseClusterRotation)]
    [InlineData(DispatchStrategy.EarliestFleetCompletion)]
    public async Task Selected_strategy_previews_once_and_persists_reviewed_plan(DispatchStrategy strategy)
    {
        var batch = await SeedAsync();
        var matrix = new Matrix();
        await using var context = fixture.CreateContext();
        var service = Service(context, matrix);
        var preview = await service.PreviewAsync(Request(batch, strategy));
        Assert.Equal(1, matrix.Calls);
        Assert.Equal(strategy, preview.Strategy);
        Assert.Empty(preview.UnassignedDeliveryIds);
        Assert.NotNull(preview.Metrics);
        Assert.Equal(3, preview.Metrics.Routes.Sum(x => x.DeliveryCount));
        Assert.Equal(60, preview.Metrics.ServiceSecondsPerStop);
        Assert.Contains(preview.Metrics.Warnings, x => x.Contains("existing work"));

        var plan = await service.ConfirmAsync(Reviewed(batch, preview));
        Assert.NotNull(plan);
        await using var verify = fixture.CreateContext();
        var saved = await new DispatchRepository(verify).GetByIdAsync(plan.Id);
        Assert.NotNull(saved);
        Assert.Equal(strategy.ToString(), saved.Strategy);
        Assert.Equal(60, saved.ServiceSecondsPerStop);
        Assert.Equal(6, saved.Routes.Sum(x => x.Stops.Count));
        if (strategy != DispatchStrategy.WarehouseClusterRotation)
            Assert.False(await verify.DispatchRotations.AnyAsync(x => x.WarehouseKey == FleetOptimizer.WarehouseKey(batch.Warehouse)));
    }

    [Fact]
    public async Task Previews_do_not_consume_rotation_and_confirmation_rotates_stable_riders()
    {
        var batch = await SeedAsync();
        await using var context = fixture.CreateContext();
        var service = Service(context, new Matrix());
        var first = await service.PreviewAsync(Request(batch));
        var again = await service.PreviewAsync(Request(batch));
        Assert.Equal(0, first.RotationVersion);
        Assert.Equal(first.RotationVersion, again.RotationVersion);
        Assert.Equal(first.Routes.SelectMany(x => x.Stops), again.Routes.SelectMany(x => x.Stops));
        Assert.False(await context.DispatchRotations.AnyAsync(x => x.WarehouseKey == FleetOptimizer.WarehouseKey(batch.Warehouse)));
        var firstDriver = first.Routes.Single(x => x.Stops.Any(s => s.DeliveryId == batch.Deliveries[0].Id)).DriverId;
        Assert.Equal(batch.Drivers.Select(x => x.Id).Min(), firstDriver);
        Assert.NotNull(await service.ConfirmAsync(Reviewed(batch, first)));

        var nextBatch = batch with { Deliveries = await AddDeliveries(batch.Warehouse) };
        await using var nextContext = fixture.CreateContext();
        var nextService = Service(nextContext, new Matrix());
        var second = await nextService.PreviewAsync(Request(nextBatch));
        Assert.Equal(1, second.RotationVersion);
        var secondDriver = second.Routes.Single(x => x.Stops.Any(s => s.DeliveryId == nextBatch.Deliveries[0].Id)).DriverId;
        Assert.NotEqual(firstDriver, secondDriver);
        Assert.NotNull(await nextService.ConfirmAsync(Reviewed(nextBatch, second)));
        var rotation = await new DispatchRepository(nextContext).ReadRotationAsync(FleetOptimizer.WarehouseKey(batch.Warehouse));
        Assert.Equal(2, rotation.Version);
        Assert.Equal(secondDriver, rotation.LastFirstDriverId);
    }

    [Fact]
    public async Task Concurrent_confirmations_of_disjoint_batches_consume_exactly_one_turn()
    {
        var firstBatch = await SeedAsync();
        var secondBatch = firstBatch with { Deliveries = await AddDeliveries(firstBatch.Warehouse) };
        async Task<DispatchPreviewResult> Preview(Batch batch)
        {
            await using var db = fixture.CreateContext();
            return await Service(db, new Matrix()).PreviewAsync(Request(batch));
        }
        var first = await Preview(firstBatch);
        var second = await Preview(secondBatch);
        async Task<bool> Confirm(Batch batch, DispatchPreviewResult preview)
        {
            await using var db = fixture.CreateContext();
            return await Service(db, new Matrix()).ConfirmAsync(Reviewed(batch, preview)) is not null;
        }
        var results = await Task.WhenAll(Confirm(firstBatch, first), Confirm(secondBatch, second));
        Assert.Equal(1, results.Count(x => x));
        await using var verify = fixture.CreateContext();
        var rotation = await new DispatchRepository(verify).ReadRotationAsync(FleetOptimizer.WarehouseKey(firstBatch.Warehouse));
        Assert.Equal(1, rotation.Version);
        var allIds = firstBatch.Deliveries.Concat(secondBatch.Deliveries).Select(x => x.Id).ToArray();
        Assert.Equal(6, await verify.DispatchStops.CountAsync(x => allIds.Contains(x.DeliveryId)));
        var loserBatch = results[0] ? secondBatch : firstBatch;
        var matrix = new Matrix();
        Assert.Null(await Service(verify, matrix).ConfirmAsync(Reviewed(loserBatch, results[0] ? second : first)));
        Assert.Equal(0, matrix.Calls);
    }

    [Fact]
    public async Task Failed_confirmations_do_not_advance_rotation_or_leave_assignments()
    {
        var batch = await SeedAsync();
        await using var context = fixture.CreateContext();
        var service = Service(context, new Matrix());
        var preview = await service.PreviewAsync(Request(batch));
        var reviewed = Reviewed(batch, preview);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ConfirmAsync(reviewed with { RotationVersion = null }));
        Assert.Null(await service.ConfirmAsync(reviewed with { RotationVersion = 42 }));

        await using (var change = fixture.CreateContext())
        {
            var vehicle = await change.Vehicles.SingleAsync(x => x.Id == batch.Vehicles[0].Id);
            vehicle.UpdateDetails("Van", 1, null, VehicleAvailability.Available, null);
            await change.SaveChangesAsync();
        }
        Assert.Null(await service.ConfirmAsync(reviewed));
        await using var verify = fixture.CreateContext();
        Assert.False(await verify.DispatchRotations.AnyAsync(x => x.WarehouseKey == FleetOptimizer.WarehouseKey(batch.Warehouse)));
        var ids = batch.Deliveries.Select(x => x.Id).ToArray();
        Assert.All(await verify.Deliveries.Where(x => ids.Contains(x.Id)).ToArrayAsync(), x => Assert.Equal(DeliveryStatus.Pending, x.Status));
    }

    [Fact]
    public async Task Repository_rechecks_cumulative_load_inside_confirmation_transaction()
    {
        var batch = await SeedAsync();
        await using var previewDb = fixture.CreateContext();
        var service = Service(previewDb, new Matrix());
        var preview = await service.PreviewAsync(Request(batch));
        // Construct a valid plan at application level, then reduce capacity before its transaction.
        var first = batch.Deliveries[0]; var second = batch.Deliveries[1];
        var rider = batch.Drivers.Single(x => x.AssignedVehicleId == batch.Vehicles[0].Id);
        var stops = new[]
        {
            Dalan.Domain.Dispatch.DispatchStop.Create(first.Id, Dalan.Domain.Dispatch.DispatchStopKind.Pickup, 0, batch.Warehouse.Latitude, 0),
            Dalan.Domain.Dispatch.DispatchStop.Create(second.Id, Dalan.Domain.Dispatch.DispatchStopKind.Pickup, 1, batch.Warehouse.Latitude, 0),
            Dalan.Domain.Dispatch.DispatchStop.Create(first.Id, Dalan.Domain.Dispatch.DispatchStopKind.Dropoff, 2, first.DropoffLatitude, 0),
            Dalan.Domain.Dispatch.DispatchStop.Create(second.Id, Dalan.Domain.Dispatch.DispatchStopKind.Dropoff, 3, second.DropoffLatitude, 0)
        };
        var route = Dalan.Domain.Dispatch.DispatchRoute.Create(batch.Vehicles[0].Id, rider.Id, batch.Warehouse.Latitude, 0, 100, 10, stops);
        var plan = Dalan.Domain.Dispatch.DispatchPlan.Create([route], DateTimeOffset.UtcNow,
            new("WorkloadEquity"));
        await using (var change = fixture.CreateContext())
        {
            var vehicle = await change.Vehicles.SingleAsync(x => x.Id == batch.Vehicles[0].Id);
            vehicle.UpdateDetails("Van", 3, null, VehicleAvailability.Available, null); // Each 2 kg job fits; both together do not.
            await change.SaveChangesAsync();
        }
        await using var confirm = fixture.CreateContext();
        Assert.False(await new DispatchRepository(confirm).ConfirmAsync(plan,
            preview.DeliveryVersions.Where(x => x.DeliveryId == first.Id || x.DeliveryId == second.Id).ToArray()));
        await using var verify = fixture.CreateContext();
        Assert.False(await verify.DispatchPlans.AnyAsync(x => x.Id == plan.Id));
    }

    [Fact]
    public async Task Unavailable_riders_invalidate_confirmation_without_advancing_rotation()
    {
        var batch = await SeedAsync();
        await using var context = fixture.CreateContext();
        var service = Service(context, new Matrix());
        var preview = await service.PreviewAsync(Request(batch));
        await using (var change = fixture.CreateContext())
        {
            var drivers = await change.Drivers.Where(x => batch.Vehicles.Select(v => v.Id).Contains(x.AssignedVehicleId!.Value)).ToArrayAsync();
            foreach (var driver in drivers) driver.Deactivate();
            await change.SaveChangesAsync();
        }
        Assert.Null(await service.ConfirmAsync(Reviewed(batch, preview)));
        await using var verify = fixture.CreateContext();
        Assert.False(await verify.DispatchRotations.AnyAsync(x => x.WarehouseKey == FleetOptimizer.WarehouseKey(batch.Warehouse)));
    }

    private DispatchService Service(DalanDbContext db, Matrix matrix)
        => new(new DispatchRepository(db), new FleetOptimizer(matrix), matrix, TimeProvider.System);

    private static DispatchPreviewRequest Request(Batch batch, DispatchStrategy strategy = DispatchStrategy.WarehouseClusterRotation)
        => new(batch.Vehicles.Select(x => new DispatchVehicleStart(x.Id, batch.Warehouse)).ToArray(),
            batch.Deliveries.Select(x => x.Id).ToArray(), strategy, Settings(batch));

    private static DispatchStrategySettings Settings(Batch batch)
        => new(new CoordinateRequest(batch.Warehouse.Latitude, batch.Warehouse.Longitude), 60);

    private static DispatchConfirmRequest Reviewed(Batch batch, DispatchPreviewResult preview)
        => new(Request(batch).Vehicles,
            preview.Routes.Select(x => new DispatchReviewedRoute(x.VehicleId,
                x.Stops.Select(s => new DispatchReviewedStop(s.DeliveryId, s.Kind)).ToArray())).ToArray(),
            preview.DeliveryVersions, preview.Strategy, Settings(batch), preview.RotationVersion);

    private async Task<Batch> SeedAsync()
    {
        var warehouse = new GeoPoint(15 + Interlocked.Increment(ref nextWarehouse) * .01, 0);
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var vehicles = Enumerable.Range(0, 2).Select(x => Vehicle.Register($"M7-{suffix}-{x}", "Van", 20)).ToArray();
        var drivers = vehicles.Select((v, i) =>
        {
            var driver = Driver.Register("M7 Driver", "09123456789", $"M7-{suffix}-{i}", DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1));
            driver.AssignVehicle(v.Id);
            return driver;
        }).ToArray();
        await using (var db = fixture.CreateContext())
        {
            db.Vehicles.AddRange(vehicles); db.Drivers.AddRange(drivers);
            await db.SaveChangesAsync();
        }
        return new(warehouse, vehicles, drivers, await AddDeliveries(warehouse));
    }

    private async Task<Delivery[]> AddDeliveries(GeoPoint warehouse)
    {
        var deliveries = new[] { .01, .02, .2 }.Select(delta => Delivery.Create("Recipient", "Warehouse", warehouse.Latitude, 0,
            "Destination", warehouse.Latitude + delta, 0, DateTimeOffset.UtcNow.AddMinutes(-1), 2)).ToArray();
        await using var db = fixture.CreateContext();
        db.Deliveries.AddRange(deliveries); await db.SaveChangesAsync();
        return deliveries;
    }

    private sealed record Batch(GeoPoint Warehouse, Vehicle[] Vehicles, Driver[] Drivers, Delivery[] Deliveries);

    private sealed class Matrix : ITravelMatrixService
    {
        public int Calls { get; private set; }
        public Task<TravelMetric?[,]> GetMatrixAsync(IReadOnlyList<GeoPoint> points, CancellationToken cancellationToken = default)
        {
            Calls++;
            var matrix = new TravelMetric?[points.Count, points.Count];
            for (var from = 0; from < points.Count; from++)
            for (var to = 0; to < points.Count; to++)
            {
                var distance = 10000 * Math.Abs(points[from].Latitude - points[to].Latitude);
                matrix[from, to] = new(distance * 10, distance);
            }
            return Task.FromResult(matrix);
        }
    }
}
