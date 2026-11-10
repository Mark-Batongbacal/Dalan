using Dalan.Application.Deliveries;
using Dalan.Application.Dispatch;
using Dalan.Domain.Deliveries;
using Dalan.Domain.Dispatch;
using Dalan.Domain.Drivers;
using Dalan.Domain.Vehicles;
using Dalan.Infrastructure.Repositories;
using Dalan.Application.FleetOptimization;
using Dalan.Application.Routing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Dalan.IntegrationTests;

[Collection("SQL Server")]
public sealed class DispatchRepositoryTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Confirmation_persists_assignment_and_stops_and_rejects_duplicate()
    {
        var (vehicle, driver, delivery, version) = await SeedAsync();
        var plan = CreatePlan(vehicle, driver, delivery);

        await using (var context = fixture.CreateContext())
        {
            Assert.True(await new DispatchRepository(context)
                .ConfirmAsync(plan, [version]));
        }

        await using (var context = fixture.CreateContext())
        {
            var repository = new DispatchRepository(context);
            var saved = await repository.GetByIdAsync(plan.Id);

            Assert.NotNull(saved);
            var route = Assert.Single(saved.Routes);
            Assert.Equal(vehicle.Id, route.VehicleId);
            Assert.Equal(driver.Id, route.DriverId);
            var reassignment = await new DeliveryService(
        new DeliveryRepository(context),
        new DriverRepository(context),
        new VehicleRepository(context),
        TimeProvider.System)
        .AssignAsync(
        delivery.Id,
        new AssignDeliveryCommand(driver.Id, vehicle.Id));

            Assert.Equal(DeliveryMutationStatus.Conflict, reassignment.Status);
            Assert.Collection(
                route.Stops.OrderBy(stop => stop.Position),
                stop => Assert.Equal(DispatchStopKind.Pickup, stop.Kind),
                stop => Assert.Equal(DispatchStopKind.Dropoff, stop.Kind));

            var assigned = await context.Deliveries.AsNoTracking()
                .SingleAsync(item => item.Id == delivery.Id);
            Assert.Equal(DeliveryStatus.Assigned, assigned.Status);
            Assert.Equal(vehicle.Id, assigned.VehicleId);
            Assert.Equal(driver.Id, assigned.DriverId);

            var currentVersion = new DispatchDeliveryVersion(
                delivery.Id, assigned.RowVersion.ToArray());
            var secondPlan = CreatePlan(vehicle, driver, delivery);

            Assert.False(await repository.ConfirmAsync(
                secondPlan, [currentVersion]));
            Assert.Null(await repository.GetByIdAsync(secondPlan.Id));
        }
    }

    [Fact]
    public async Task Confirmation_rejects_a_stale_delivery_version()
    {
        var (vehicle, driver, delivery, version) = await SeedAsync();

        await using (var context = fixture.CreateContext())
        {
            var repository = new DeliveryRepository(context);
            var changed = await repository.GetByIdAsync(delivery.Id);
            Assert.NotNull(changed);
            changed.Cancel(DateTimeOffset.UtcNow);
            Assert.Equal(
                SaveDeliveryOutcome.Saved,
                await repository.UpdateAsync(changed));
        }

        await using (var context = fixture.CreateContext())
        {
            var repository = new DispatchRepository(context);
            var plan = CreatePlan(vehicle, driver, delivery);

            Assert.False(await repository.ConfirmAsync(plan, [version]));
            Assert.Null(await repository.GetByIdAsync(plan.Id));
        }
    }

    [Fact]
    public async Task Concurrent_confirmations_create_only_one_plan()
    {
        var (vehicle, driver, delivery, version) = await SeedAsync();
        var first = CreatePlan(vehicle, driver, delivery);
        var second = CreatePlan(vehicle, driver, delivery);

        async Task<bool> Confirm(DispatchPlan plan)
        {
            await using var context = fixture.CreateContext();
            return await new DispatchRepository(context)
                .ConfirmAsync(plan, [version]);
        }

        var results = await Task.WhenAll(Confirm(first), Confirm(second));

        Assert.Equal(1, results.Count(success => success));

        await using var verify = fixture.CreateContext();
        Assert.Equal(1, await verify.DispatchPlans.AsNoTracking()
            .CountAsync(plan => plan.Id == first.Id || plan.Id == second.Id));
        Assert.Equal(2, await verify.DispatchStops.AsNoTracking()
            .CountAsync(stop => stop.DeliveryId == delivery.Id));
    }

    [Fact]
    public async Task Preview_and_reviewed_override_persist_chosen_stop_order()
    {
        var (vehicle, _, first, _) = await SeedAsync();
        var second = Delivery.Create(
            "Second Recipient", "Second Pickup", 7.3, 125.6,
            "Second Destination", 7.4, 125.7,
            DateTimeOffset.UtcNow.AddMinutes(-1), 10);

        await using (var seed = fixture.CreateContext())
        {
            seed.Deliveries.Add(second);
            await seed.SaveChangesAsync();
        }

        await using var context = fixture.CreateContext();
        var matrix = new LineMatrix();
        var dispatch = new DispatchService(
            new DispatchRepository(context),
            new FleetOptimizer(matrix),
            matrix,
            TimeProvider.System);

        var vehicles = new[]
        {
        new DispatchVehicleStart(vehicle.Id, new GeoPoint(7.0, 125.6))
    };

        var preview = await dispatch.PreviewAsync(
            new DispatchPreviewRequest(vehicles, [first.Id, second.Id]));

        Assert.Empty(preview.UnassignedDeliveryIds);
        Assert.Equal(2, preview.DeliveryVersions.Count);

        var confirmed = await dispatch.ConfirmAsync(
            new DispatchConfirmRequest(
                vehicles,
                [
                    new DispatchReviewedRoute(vehicle.Id,
                [
                    new(second.Id, DispatchStopKind.Pickup),
                    new(first.Id, DispatchStopKind.Pickup),
                    new(second.Id, DispatchStopKind.Dropoff),
                    new(first.Id, DispatchStopKind.Dropoff)
                ])
                ],
                preview.DeliveryVersions));

        Assert.NotNull(confirmed);

        await using var verify = fixture.CreateContext();
        var saved = await new DispatchRepository(verify)
            .GetByIdAsync(confirmed.Id);
        Assert.NotNull(saved);

        var route = Assert.Single(saved.Routes);
        var stops = route.Stops.OrderBy(stop => stop.Position).ToArray();

        Assert.Equal(
            new[] { second.Id, first.Id, second.Id, first.Id },
            stops.Select(stop => stop.DeliveryId).ToArray());
        Assert.Equal(
            new[]
            {
            DispatchStopKind.Pickup, DispatchStopKind.Pickup,
            DispatchStopKind.Dropoff, DispatchStopKind.Dropoff
            },
            stops.Select(stop => stop.Kind).ToArray());
        Assert.Equal(1_000, route.DistanceMeters, 6);
        Assert.Equal(60, route.DurationSeconds, 6);
    }
        private sealed class LineMatrix(bool unreachable = false)
        : ITravelMatrixService
        {
            public int Calls { get; private set; }

            public Task<TravelMetric?[,]> GetMatrixAsync(
                IReadOnlyList<GeoPoint> points,
                CancellationToken cancellationToken = default)
            {
                Calls++;
                var result = new TravelMetric?[points.Count, points.Count];

                for (var from = 0; from < points.Count; from++)
                    for (var to = 0; to < points.Count; to++)
                    {
                        if (unreachable && from != to)
                            continue;

                        var difference = Math.Abs(
                            points[from].Latitude - points[to].Latitude);
                        result[from, to] = new TravelMetric(
                            difference * 1_000,
                            difference * 60);
                    }

                return Task.FromResult(result);
            }
        }

    [Fact]
    public async Task Preview_reports_unreachable_delivery_without_saving_a_plan()
    {
        var (vehicle, _, delivery, _) = await SeedAsync();

        await using var context = fixture.CreateContext();
        var matrix = new LineMatrix(unreachable: true);
        var dispatch = new DispatchService(
            new DispatchRepository(context),
            new FleetOptimizer(matrix),
            matrix,
            TimeProvider.System);

        var preview = await dispatch.PreviewAsync(
            new DispatchPreviewRequest(
                [new(vehicle.Id, new GeoPoint(7.0, 125.6))],
                [delivery.Id]));

        Assert.Equal([delivery.Id], preview.UnassignedDeliveryIds);
        Assert.Empty(Assert.Single(preview.Routes).Stops);
        Assert.False(await context.DispatchPlans.AsNoTracking().AnyAsync(
            plan => plan.Routes.Any(route =>
                route.Stops.Any(stop => stop.DeliveryId == delivery.Id))));
    }

    [Fact]
    public async Task Confirm_accepts_preview_with_an_unassigned_delivery()
    {
        var (vehicle, _, feasible, _) = await SeedAsync();
        var tooHeavy = Delivery.Create(
            "Heavy Recipient", "Heavy Pickup", 7.3, 125.6,
            "Heavy Destination", 7.4, 125.7,
            DateTimeOffset.UtcNow.AddMinutes(-1), 200);

        await using (var seed = fixture.CreateContext())
        {
            seed.Deliveries.Add(tooHeavy);
            await seed.SaveChangesAsync();
        }

        await using var context = fixture.CreateContext();
        var matrix = new LineMatrix();
        var dispatch = new DispatchService(
            new DispatchRepository(context),
            new FleetOptimizer(matrix),
            matrix,
            TimeProvider.System);
        var vehicles = new[]
        {
            new DispatchVehicleStart(vehicle.Id, new GeoPoint(7.0, 125.6))
        };

        var preview = await dispatch.PreviewAsync(
            new DispatchPreviewRequest(vehicles, [feasible.Id, tooHeavy.Id]));

        Assert.Equal([tooHeavy.Id], preview.UnassignedDeliveryIds);
        Assert.Equal(2, preview.DeliveryVersions.Count);

        var confirmed = await dispatch.ConfirmAsync(new DispatchConfirmRequest(
            vehicles,
            [new DispatchReviewedRoute(vehicle.Id,
                [new(feasible.Id, DispatchStopKind.Pickup),
                 new(feasible.Id, DispatchStopKind.Dropoff)])],
            preview.DeliveryVersions));

        Assert.NotNull(confirmed);
        await using var verify = fixture.CreateContext();
        Assert.Equal(2, await verify.DispatchStops.AsNoTracking()
            .CountAsync(stop => stop.DeliveryId == feasible.Id));
        Assert.False(await verify.DispatchStops.AsNoTracking()
            .AnyAsync(stop => stop.DeliveryId == tooHeavy.Id));
    }

    [Fact]
    public async Task Confirming_preassigned_delivery_invalidates_stale_reassignment()
    {
        var (vehicle, driver, delivery, _) = await SeedAsync();

        await using (var setup = fixture.CreateContext())
        {
            var current = await setup.Deliveries
                .SingleAsync(item => item.Id == delivery.Id);
            current.Assign(driver.Id, vehicle.Id, DateTimeOffset.UtcNow);
            await setup.SaveChangesAsync();
        }

        await using var reassignmentContext = fixture.CreateContext();
        var deliveryRepository = new DeliveryRepository(reassignmentContext);
        var stale = await deliveryRepository.GetByIdAsync(delivery.Id);
        Assert.NotNull(stale);
        Assert.False(await deliveryRepository
            .IsInConfirmedPlanAsync(delivery.Id));

        var version = new DispatchDeliveryVersion(
            delivery.Id, stale.RowVersion.ToArray());
        var plan = CreatePlan(vehicle, driver, delivery);

        await using (var confirmContext = fixture.CreateContext())
        {
            Assert.True(await new DispatchRepository(confirmContext)
                .ConfirmAsync(plan, [version]));
        }

        stale.Assign(
            driver.Id, vehicle.Id, DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Equal(
            SaveDeliveryOutcome.Conflict,
            await deliveryRepository.UpdateAsync(stale));
    }

    [Fact]
    public async Task Invalid_reviewed_order_is_rejected_before_routing()
    {
        var (vehicle, _, delivery, version) = await SeedAsync();
        await using var context = fixture.CreateContext();
        var matrix = new LineMatrix();
        var dispatch = new DispatchService(
            new DispatchRepository(context), new FleetOptimizer(matrix),
            matrix, TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentException>(() => dispatch.ConfirmAsync(
            new DispatchConfirmRequest(
                [new DispatchVehicleStart(vehicle.Id, new GeoPoint(7.0, 125.6))],
                [new DispatchReviewedRoute(vehicle.Id,
                    [new(delivery.Id, DispatchStopKind.Dropoff),
                     new(delivery.Id, DispatchStopKind.Pickup)])],
                [version])));

        Assert.Equal(0, matrix.Calls);
    }

    private async Task<(Vehicle Vehicle, Driver Driver, Delivery Delivery,

        DispatchDeliveryVersion Version)> SeedAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var vehicle = Vehicle.Register("DIS-" + suffix, "Van", 100);
        var driver = Driver.Register(
            "Dispatch Driver", "09123456789", "DIS-LIC-" + suffix,
            DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1));
        driver.AssignVehicle(vehicle.Id);

        var delivery = Delivery.Create(
            "Recipient", "Warehouse", 7.1, 125.6,
            "Destination", 7.2, 125.7,
            DateTimeOffset.UtcNow.AddMinutes(-1), 10);

        await using var context = fixture.CreateContext();
        context.Vehicles.Add(vehicle);
        context.Drivers.Add(driver);
        context.Deliveries.Add(delivery);
        await context.SaveChangesAsync();

        return (vehicle, driver, delivery,
            new DispatchDeliveryVersion(
                delivery.Id, delivery.RowVersion.ToArray()));
    }

    private static DispatchPlan CreatePlan(
        Vehicle vehicle, Driver driver, Delivery delivery)
    {
        var route = DispatchRoute.Create(
            vehicle.Id, driver.Id, 7.1, 125.6, 1_000, 120,
            [
                DispatchStop.Create(
                    delivery.Id, DispatchStopKind.Pickup, 0, 7.1, 125.6),
                DispatchStop.Create(
                    delivery.Id, DispatchStopKind.Dropoff, 1, 7.2, 125.7)
            ]);

        return DispatchPlan.Create([route], DateTimeOffset.UtcNow);
    }
}
