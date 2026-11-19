using Dalan.Application.Tracking;
using Dalan.Domain.Deliveries;
using Dalan.Domain.Drivers;
using Dalan.Domain.Vehicles;
using Dalan.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Dalan.IntegrationTests;

[Collection("SQL Server")]
public sealed class FleetTrackingSqlTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Tracking_is_owned_idempotent_measured_and_stops_after_delivery()
    {
        var now = new DateTimeOffset(2026, 11, 10, 10, 0, 0, TimeSpan.Zero);
        var suffix = Guid.NewGuid().ToString("N");
        var vehicle = Vehicle.Register("GPS-" + suffix[..8]);
        var driver = Driver.Register("GPS Driver", "123", suffix, DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1));
        driver.AssignVehicle(vehicle.Id);
        var delivery = Delivery.Create("Test", "Warehouse", 14, 121, "Dropoff", 14.01, 121, now);
        delivery.Assign(driver.Id, vehicle.Id, now);
        await using var db = fixture.CreateContext();
        db.AddRange(vehicle, driver, delivery); await db.SaveChangesAsync();
        var repository = new FleetTrackingRepository(db);
        var session = (await repository.StartAsync(driver.Id, now, default))!;
        Assert.NotNull(session);
        Assert.Equal(session, await repository.StartAsync(driver.Id, now, default));
        Assert.Null(await repository.StartAsync(Guid.NewGuid(), now, default));
        var first = new TrackingFix(Guid.NewGuid(), now, 14, 121, 5);
        Assert.Equal(TrackingWriteStatus.Missing, await repository.RecordAsync(Guid.NewGuid(), session.SessionId, first, now, default));
        Assert.Equal(TrackingWriteStatus.Accepted, await repository.RecordAsync(driver.Id, session.SessionId, first, now, default));
        Assert.Equal(TrackingWriteStatus.Duplicate, await repository.RecordAsync(driver.Id, session.SessionId, first, now, default));
        Assert.Equal(TrackingWriteStatus.Conflict, await repository.RecordAsync(driver.Id, session.SessionId, first with { Latitude = 14.1 }, now, default));
        Assert.Equal(TrackingWriteStatus.TooFrequent, await repository.RecordAsync(driver.Id, session.SessionId, first with { SampleId = Guid.NewGuid(), CapturedAtUtc = now.AddSeconds(1) }, now.AddSeconds(1), default));
        var moving = first with { SampleId = Guid.NewGuid(), CapturedAtUtc = now.AddSeconds(30), Latitude = 14.001 };
        Assert.Equal(TrackingWriteStatus.Accepted, await repository.RecordAsync(driver.Id, session.SessionId, moving, now.AddSeconds(30), default));
        var stationary = moving with { SampleId = Guid.NewGuid(), CapturedAtUtc = now.AddSeconds(60) };
        Assert.Equal(TrackingWriteStatus.Accepted, await repository.RecordAsync(driver.Id, session.SessionId, stationary, now.AddSeconds(60), default));
        var activity = await repository.ActivityAsync(now.AddSeconds(15), now.AddSeconds(45), default);
        Assert.Equal(30, activity.ObservedSeconds); Assert.Equal(15, activity.MovingSeconds);
        Assert.InRange(activity.GpsDistanceMeters, 55, 56); Assert.Equal(2, activity.Samples);
        var fresh = Assert.Single(await repository.PositionsAsync(now.AddSeconds(60), default), x => x.VehicleId == vehicle.Id);
        Assert.Equal("Fresh", fresh.Status);
        Assert.Equal("Stale", Assert.Single(await repository.PositionsAsync(now.AddMinutes(3), default), x => x.VehicleId == vehicle.Id).Status);
        delivery.MarkPickedUp(driver.Id, now.AddSeconds(61));
        delivery.Complete(driver.Id, "test-photo", "Recipient", 14.01, 121, now.AddSeconds(62));
        await db.SaveChangesAsync();
        var next = stationary with { SampleId = Guid.NewGuid(), CapturedAtUtc = now.AddSeconds(90) };
        Assert.Equal(TrackingWriteStatus.Stopped, await repository.RecordAsync(driver.Id, session.SessionId, next, now.AddSeconds(90), default));
        Assert.Equal(TrackingWriteStatus.Stopped, await repository.RecordAsync(driver.Id, session.SessionId, first, now.AddSeconds(90), default));
        Assert.Equal("Sharing stopped", Assert.Single(await repository.PositionsAsync(now.AddSeconds(90), default), x => x.VehicleId == vehicle.Id).Status);
        Assert.Null(await repository.StartAsync(driver.Id, now.AddSeconds(90), default));
        Assert.Equal(3, await db.TrackingSamples.CountAsync(x => x.SessionId == session.SessionId));
        await repository.CleanupAsync(now.AddDays(31), default);
        Assert.False(await db.TrackingSessions.AnyAsync(x => x.Id == session.SessionId));
        Assert.False(await db.TrackingSamples.AnyAsync(x => x.SessionId == session.SessionId));
    }

    [Fact]
    public async Task Concurrent_start_uses_one_open_driver_session_and_reassignment_replaces_it()
    {
        var now = DateTimeOffset.UtcNow;
        var suffix = Guid.NewGuid().ToString("N");
        var vehicle = Vehicle.Register("GP1-" + suffix[..7]);
        var nextVehicle = Vehicle.Register("GP2-" + suffix[..7]);
        var driver = Driver.Register("Concurrent GPS", "123", suffix, DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1));
        driver.AssignVehicle(vehicle.Id);
        var delivery = Delivery.Create("Test", "Warehouse", 14, 121, "Dropoff", 14.01, 121, now);
        delivery.Assign(driver.Id, vehicle.Id, now);
        await using (var seed = fixture.CreateContext()) { seed.AddRange(vehicle, nextVehicle, driver, delivery); await seed.SaveChangesAsync(); }
        async Task<TrackingStart?> Start()
        { await using var context = fixture.CreateContext(); return await new FleetTrackingRepository(context).StartAsync(driver.Id, now, default); }
        var starts = await Task.WhenAll(Start(), Start());
        Assert.NotNull(starts[0]); Assert.Equal(starts[0], starts[1]);
        await using var db = fixture.CreateContext();
        var repository = new FleetTrackingRepository(db);
        await repository.RecordAsync(driver.Id, starts[0]!.SessionId, new(Guid.NewGuid(), now, 14, 121, 5), now, default);
        var current = await db.Drivers.SingleAsync(x => x.Id == driver.Id);
        current.AssignVehicle(nextVehicle.Id);
        var other = Delivery.Create("New", "Warehouse", 14, 121, "Dropoff", 14.01, 121, now);
        other.Assign(driver.Id, nextVehicle.Id, now);
        db.Add(other); await db.SaveChangesAsync();
        Assert.Equal("Sharing stopped", Assert.Single(await repository.PositionsAsync(now, default), x => x.VehicleId == vehicle.Id).Status);
        var replacement = await repository.StartAsync(driver.Id, now.AddSeconds(1), default);
        Assert.NotNull(replacement); Assert.Equal(nextVehicle.Id, replacement.VehicleId);
        Assert.NotEqual(starts[0]!.SessionId, replacement.SessionId);
        Assert.Equal(1, await db.TrackingSessions.CountAsync(x => x.DriverId == driver.Id && x.EndedAtUtc == null));
        await repository.StopAsync(Guid.NewGuid(), replacement.SessionId, now, default);
        Assert.True(await db.TrackingSessions.AnyAsync(x => x.Id == replacement.SessionId && x.EndedAtUtc == null));
        await repository.StopAsync(driver.Id, replacement.SessionId, now, default);
        Assert.False(await db.TrackingSessions.AnyAsync(x => x.Id == replacement.SessionId && x.EndedAtUtc == null));
    }
}
