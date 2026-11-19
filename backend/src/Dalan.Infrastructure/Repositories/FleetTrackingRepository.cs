using System.Security.Cryptography;
using System.Text.Json;
using Dalan.Application.Tracking;
using Dalan.Domain.Deliveries;
using Dalan.Domain.Drivers;
using Dalan.Domain.Tracking;
using Dalan.Domain.Vehicles;
using Dalan.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Dalan.Infrastructure.Repositories;

public sealed class FleetTrackingRepository(DalanDbContext db) : IFleetTrackingRepository
{
    private async Task<Guid?> VehicleAsync(Guid driverId, CancellationToken token) => await (
        from driver in db.Drivers.AsNoTracking()
        join vehicle in db.Vehicles.AsNoTracking() on driver.AssignedVehicleId equals vehicle.Id
        where driver.Id == driverId && driver.Availability != DriverAvailability.Inactive && vehicle.Availability != VehicleAvailability.Inactive
        select (Guid?)vehicle.Id).SingleOrDefaultAsync(token);

    private Task<bool> ActiveAsync(Guid driverId, Guid vehicleId, CancellationToken token) => db.Deliveries.AnyAsync(x =>
        x.DriverId == driverId && x.VehicleId == vehicleId && (x.Status == DeliveryStatus.Assigned || x.Status == DeliveryStatus.PickedUp), token);

    public async Task<TrackingStart?> StartAsync(Guid driverId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var vehicle = await VehicleAsync(driverId, cancellationToken);
        if (vehicle is not Guid vehicleId || !await ActiveAsync(driverId, vehicleId, cancellationToken)) return null;
        var previous = await db.TrackingSessions.SingleOrDefaultAsync(x => x.DriverId == driverId && x.EndedAtUtc == null, cancellationToken);
        if (previous is not null && previous.VehicleId == vehicleId) return new(previous.Id, vehicleId);
        var session = VehicleTrackingSession.Start(driverId, vehicleId, now);
        try
        {
            // Release the filtered unique driver slot before inserting its replacement.
            if (previous is not null) { previous.Stop(now); await db.SaveChangesAsync(cancellationToken); }
            db.TrackingSessions.Add(session);
            await db.SaveChangesAsync(cancellationToken);
            return new(session.Id, vehicleId);
        }
        catch (DbUpdateException error) when (error is DbUpdateConcurrencyException || error.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear();
            var current = await db.TrackingSessions.AsNoTracking().SingleOrDefaultAsync(x => x.DriverId == driverId && x.EndedAtUtc == null, cancellationToken);
            return current?.VehicleId == vehicleId ? new(current.Id, vehicleId) : null;
        }
    }

    public async Task<TrackingWriteStatus> RecordAsync(Guid driverId, Guid sessionId, TrackingFix fix, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var session = await db.TrackingSessions.SingleOrDefaultAsync(x => x.Id == sessionId && x.DriverId == driverId, cancellationToken);
        if (session is null || await VehicleAsync(driverId, cancellationToken) != session.VehicleId) return TrackingWriteStatus.Missing;
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(fix with { CapturedAtUtc = fix.CapturedAtUtc.ToUniversalTime() })));
        var replay = await ReadReplay();
        if (replay == TrackingWriteStatus.Conflict) return TrackingWriteStatus.Conflict;
        if (session.EndedAtUtc is not null) return TrackingWriteStatus.Stopped;
        if (!await ActiveAsync(driverId, session.VehicleId, cancellationToken))
        { session.Stop(now); await db.SaveChangesAsync(cancellationToken); return TrackingWriteStatus.Stopped; }
        if (replay == TrackingWriteStatus.Duplicate) return TrackingWriteStatus.Duplicate;
        if (session.LastCapturedAtUtc is DateTimeOffset last && fix.CapturedAtUtc > last && fix.CapturedAtUtc - last < TimeSpan.FromSeconds(5))
            return TrackingWriteStatus.TooFrequent;
        try { db.TrackingSamples.Add(session.Observe(fix.SampleId, fix.Latitude, fix.Longitude, fix.AccuracyMeters, fix.CapturedAtUtc, now, hash)); }
        catch (ArgumentException) { return TrackingWriteStatus.Invalid; }
        catch (InvalidOperationException) { return TrackingWriteStatus.Conflict; }
        try { await db.SaveChangesAsync(cancellationToken); return TrackingWriteStatus.Accepted; }
        catch (DbUpdateException error) when (error is DbUpdateConcurrencyException || error.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear();
            return await ReadReplay() ?? TrackingWriteStatus.Conflict;
        }

        async Task<TrackingWriteStatus?> ReadReplay()
        {
            var existing = await db.TrackingSamples.AsNoTracking().Where(x => x.SessionId == sessionId && x.SampleId == fix.SampleId)
                .Select(x => x.PayloadHash).SingleOrDefaultAsync(cancellationToken);
            return existing is null ? null : existing == hash ? TrackingWriteStatus.Duplicate : TrackingWriteStatus.Conflict;
        }
    }

    public async Task StopAsync(Guid driverId, Guid sessionId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await db.TrackingSessions.Where(x => x.Id == sessionId && x.DriverId == driverId && x.EndedAtUtc == null)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.EndedAtUtc, now), cancellationToken);
    }

    public async Task<IReadOnlyList<VehiclePosition>> PositionsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var vehicles = await db.Vehicles.AsNoTracking().OrderBy(x => x.RegistrationNumber).Take(200).Select(v => new
        {
            v.Id, v.RegistrationNumber,
            DriverName = db.Drivers.Where(d => d.AssignedVehicleId == v.Id).Select(d => d.Name).FirstOrDefault(),
            DriverId = db.Drivers.Where(d => d.AssignedVehicleId == v.Id && d.Availability != DriverAvailability.Inactive
                && v.Availability != VehicleAvailability.Inactive).Select(d => (Guid?)d.Id).FirstOrDefault(),
            SessionId = db.TrackingSessions.Where(s => s.VehicleId == v.Id).OrderByDescending(s => s.StartedAtUtc).ThenByDescending(s => s.Id)
                .Select(s => (Guid?)s.Id).FirstOrDefault()
        }).ToListAsync(cancellationToken);
        var ids = vehicles.Where(v => v.SessionId != null).Select(v => v.SessionId!.Value).ToArray();
        var sessions = await db.TrackingSessions.AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, cancellationToken);
        return vehicles.Select(v =>
        {
            var s = v.SessionId is Guid id ? sessions.GetValueOrDefault(id) : null;
            var sharing = s is not null && s.EndedAtUtc is null && s.DriverId == v.DriverId;
            var fresh = sharing && s?.LastCapturedAtUtc >= now.AddSeconds(-90);
            return new VehiclePosition(v.Id, v.RegistrationNumber, v.DriverName, s?.Latitude, s?.Longitude,
                s?.AccuracyMeters, s?.LastCapturedAtUtc, s?.LastCapturedAtUtc is null ? "No location" : !sharing ? "Sharing stopped" : fresh ? "Fresh" : "Stale");
        }).ToArray();
    }

    public async Task<ObservedFleetActivity> ActivityAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken)
    {
        var rows = db.TrackingSamples.AsNoTracking().Where(x => x.ObservedSeconds > 0 && x.CapturedAtUtc > start && x.IntervalStartUtc < end)
            .Select(x => new
            {
                Seconds = EF.Functions.DateDiffMillisecond(x.IntervalStartUtc < start ? start : x.IntervalStartUtc,
                    x.CapturedAtUtc > end ? end : x.CapturedAtUtc) / 1000d,
                x.Moving, MetersPerSecond = x.DistanceMeters / x.ObservedSeconds
            });
        var result = await rows.GroupBy(_ => 1).Select(g => new ObservedFleetActivity(g.Sum(x => x.Seconds),
            g.Sum(x => x.Moving ? x.Seconds : 0), g.Sum(x => x.MetersPerSecond * x.Seconds), g.Count())).SingleOrDefaultAsync(cancellationToken);
        return result ?? new(0, 0, 0, 0);
    }

    public async Task CleanupAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await db.TrackingSessions.Where(x => x.EndedAtUtc == null && (x.LastCapturedAtUtc ?? x.StartedAtUtc) < now.AddMinutes(-15))
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.EndedAtUtc, now), cancellationToken);
        await db.TrackingSamples.Where(x => x.CapturedAtUtc < now.AddDays(-30)).ExecuteDeleteAsync(cancellationToken);
        await db.TrackingSessions.Where(x => x.EndedAtUtc < now.AddDays(-30)).ExecuteDeleteAsync(cancellationToken);
    }
}
