using Dalan.Application.Analytics;
using Dalan.Domain.Deliveries;
using Dalan.Domain.Dispatch;
using Dalan.Domain.Drivers;
using Dalan.Domain.Vehicles;
using Dalan.Infrastructure.Repositories;
using Xunit;

namespace Dalan.IntegrationTests;

public sealed class AnalyticsRepositoryTests : IAsyncLifetime
{
    // Separate disposable database per test: report totals cannot share other tests' fixtures.
    private readonly SqlServerFixture fixture = new();
    public Task InitializeAsync() => fixture.InitializeAsync();
    public Task DisposeAsync() => fixture.DisposeAsync();
    private static DateTimeOffset At(int day, int hour = 0) => new(2040, 1, day, hour, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sql_aggregates_preserve_utc_cohort_deadlines_and_distinct_fleet_participation()
    {
        await using var db = fixture.CreateContext();
        var vehicle = Vehicle.Register("ANALYTICS-1");
        var other = Vehicle.Register("ANALYTICS-2");
        var inactive = Vehicle.Register("ANALYTICS-3"); inactive.Deactivate();
        var driver = Driver.Register("Analytics Driver", "09123456789", "AN-LIC-1", new(2041, 1, 1));
        var otherDriver = Driver.Register("Other Driver", "09123456789", "AN-LIC-2", new(2041, 1, 1));
        db.AddRange(vehicle, other, inactive, driver, otherDriver);
        Delivery Create(DateTimeOffset created, DateTimeOffset? due = null)
            => Delivery.Create("Recipient", "Pickup", 14.6, 121.03, "Drop-off", 14.61, 121.04, created, expectedDeliveryAt: due);
        Delivery Complete(DateTimeOffset created, int hours, DateTimeOffset? due)
        {
            var d = Create(created, due);
            d.Assign(driver.Id, vehicle.Id, created);
            d.MarkPickedUp(driver.Id, created.AddHours(1));
            d.Complete(driver.Id, "test-photo", "Recipient", 14.61, 121.04, created.AddHours(hours));
            return d;
        }
        var pending = Create(At(10), At(11));
        var assigned = Create(At(10), At(15, 12)); assigned.Assign(driver.Id, vehicle.Id, At(10, 1));
        var picked = Create(At(11)); picked.Assign(driver.Id, vehicle.Id, At(11, 1)); picked.MarkPickedUp(driver.Id, At(11, 2));
        var late = Complete(At(10), 3, At(10, 2));
        var exact = Complete(At(11), 2, At(11, 2));
        var noDeadline = Complete(At(12), 4, null);
        var cancelled = Create(At(12), At(13)); cancelled.Cancel(At(12, 1));
        var lastMoment = Create(At(13).AddTicks(-1));
        var before = Complete(At(9), 30, null); // Completed inside period, created outside: excluded.
        var endBoundary = Create(At(13));
        var offsetBefore = Create(new DateTimeOffset(2040, 1, 10, 0, 30, 0, TimeSpan.FromHours(8)));
        db.AddRange(pending, assigned, picked, late, exact, noDeadline, cancelled, lastMoment, before, endBoundary, offsetBefore);

        void Plan(Vehicle v, Driver who, int confirmedDay, double distance, double seconds)
        {
            var delivery = Create(At(1));
            delivery.Assign(who.Id, v.Id, At(2));
            db.Deliveries.Add(delivery);
            db.DispatchPlans.Add(DispatchPlan.Create([
                DispatchRoute.Create(v.Id, who.Id, 14.6, 121.03, distance, seconds, [
                    DispatchStop.Create(delivery.Id, DispatchStopKind.Pickup, 0, 14.6, 121.03),
                    DispatchStop.Create(delivery.Id, DispatchStopKind.Dropoff, 1, 14.61, 121.04)
                ])], At(confirmedDay)));
        }
        Plan(vehicle, driver, 11, 1000, 100);
        Plan(vehicle, driver, 12, 2500, 200);
        Plan(other, otherDriver, 13, 9000, 999); // Exactly upper bound: excluded.
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var report = await new AnalyticsRepository(db).ReadAsync(AnalyticsPeriod.Create(new(2040, 1, 10), new(2040, 1, 12)), At(15, 12));
        Assert.Equal(new DeliveryAnalytics(8, 2, 1, 1, 3, 1, 1, 1, 2, 10800), report.Deliveries);
        Assert.Equal(new FleetAnalytics(3, 2, 1, 2, 2, 3500, 300), report.Fleet);
        Assert.Equal(new[] { 3, 2, 3 }, report.DailyIntake.OrderBy(x => x.Day).Select(x => x.Count));
        Assert.Equal(3, report.Vehicles.Count);
        Assert.Equal(2, report.Vehicles.Single(x => x.VehicleId == vehicle.Id).ConfirmedRoutes);
        Assert.Equal(0, report.Vehicles.Single(x => x.VehicleId == other.Id).ConfirmedRoutes);
        Assert.Equal("Inactive", report.Vehicles.Single(x => x.VehicleId == inactive.Id).Availability);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Empty_and_future_only_periods_do_not_invent_activity()
    {
        await using var db = fixture.CreateContext();
        db.Deliveries.Add(Delivery.Create("Future", "Pickup", 0, 0, "Drop-off", 1, 1, At(16)));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var report = await new AnalyticsRepository(db).ReadAsync(AnalyticsPeriod.Create(new(2040, 1, 15), new(2040, 1, 16)), At(15, 12));
        Assert.Equal(0, report.Deliveries.Total);
        Assert.Null(report.Deliveries.AverageFulfilmentSeconds);
        Assert.Empty(report.DailyIntake);
        Assert.Equal(0, report.Fleet.TotalVehicles);
        Assert.Empty(report.Vehicles);
    }

    [Fact]
    public async Task Completion_duration_can_exceed_sql_datediff_second_integer_range()
    {
        await using var db = fixture.CreateContext();
        var vehicle = Vehicle.Register("ANALYTICS-LONG");
        var driver = Driver.Register("Long Driver", "09123456789", "AN-LONG", new(2150, 1, 1));
        var created = At(10, 12).AddMilliseconds(125);
        var completed = created.AddYears(70).AddMilliseconds(15);
        var delivery = Delivery.Create("Historic", "Pickup", 0, 0, "Drop-off", 1, 1, created);
        delivery.Assign(driver.Id, vehicle.Id, created);
        delivery.MarkPickedUp(driver.Id, created);
        delivery.Complete(driver.Id, "test-photo", "Recipient", 1, 1, completed);
        db.AddRange(vehicle, driver, delivery);
        await db.SaveChangesAsync();
        var report = await new AnalyticsRepository(db).ReadAsync(AnalyticsPeriod.Create(new(2040, 1, 10), new(2040, 1, 10)), completed.AddDays(1));
        Assert.Equal((completed - created).TotalSeconds, report.Deliveries.AverageFulfilmentSeconds!.Value, precision: 3);
    }

    [Fact]
    public async Task Vehicle_table_limit_does_not_truncate_fleet_denominator()
    {
        await using var db = fixture.CreateContext();
        db.Vehicles.AddRange(Enumerable.Range(0, 51).Select(i => Vehicle.Register($"AN-{i:D3}")));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var report = await new AnalyticsRepository(db).ReadAsync(AnalyticsPeriod.Create(new(2040, 1, 10), new(2040, 1, 10)), At(15));
        Assert.Equal(51, report.Fleet.TotalVehicles);
        Assert.Equal(51, report.Fleet.AvailableVehicles);
        Assert.Equal(50, report.Vehicles.Count);
        Assert.Equal("AN-000", report.Vehicles[0].RegistrationNumber);
        Assert.Equal("AN-049", report.Vehicles[^1].RegistrationNumber);
        Assert.All(report.Vehicles, row => Assert.Equal(0, row.ConfirmedRoutes));
    }
}
