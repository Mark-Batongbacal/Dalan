using System.Data.Common;
using Dalan.Application.Analytics;
using Dalan.Domain.Deliveries;
using Dalan.Domain.Vehicles;
using Dalan.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Dalan.Infrastructure.Repositories;

public sealed class AnalyticsRepository(DalanDbContext db) : IAnalyticsRepository
{
    public async Task<AnalyticsSnapshot> ReadAsync(AnalyticsPeriod period, DateTimeOffset asOfUtc,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var cohort = db.Deliveries.AsNoTracking().Where(d => d.CreatedAtUtc >= period.StartUtc &&
                d.CreatedAtUtc < period.EndExclusiveUtc && d.CreatedAtUtc <= asOfUtc);
            var metrics = await cohort.Select(d => new
            {
                d.Status, d.ExpectedDeliveryAtUtc, d.DeliveredAtUtc,
                // Keep the millisecond remainder within one day to avoid DATEDIFF integer overflow.
                FulfilmentSeconds = d.Status == DeliveryStatus.Delivered && d.DeliveredAtUtc != null
                    ? (double?)EF.Functions.DateDiffDay(d.CreatedAtUtc, d.DeliveredAtUtc.Value) * 86400d +
                        EF.Functions.DateDiffMillisecond(
                            d.CreatedAtUtc.AddDays(EF.Functions.DateDiffDay(d.CreatedAtUtc, d.DeliveredAtUtc.Value)),
                            d.DeliveredAtUtc.Value) / 1000d
                    : null
            }).GroupBy(_ => 1).Select(g => new DeliveryAnalytics(
                g.Count(),
                g.Count(d => d.Status == DeliveryStatus.Pending),
                g.Count(d => d.Status == DeliveryStatus.Assigned),
                g.Count(d => d.Status == DeliveryStatus.PickedUp),
                g.Count(d => d.Status == DeliveryStatus.Delivered),
                g.Count(d => d.Status == DeliveryStatus.Cancelled),
                g.Count(d => (d.Status == DeliveryStatus.Pending || d.Status == DeliveryStatus.Assigned ||
                    d.Status == DeliveryStatus.PickedUp) && d.ExpectedDeliveryAtUtc < asOfUtc),
                g.Count(d => d.Status == DeliveryStatus.Delivered && d.DeliveredAtUtc > d.ExpectedDeliveryAtUtc),
                g.Count(d => d.Status == DeliveryStatus.Delivered && d.ExpectedDeliveryAtUtc != null),
                g.Average(d => d.FulfilmentSeconds))).SingleOrDefaultAsync(cancellationToken)
                ?? new DeliveryAnalytics(0, 0, 0, 0, 0, 0, 0, 0, 0, null);

            var daily = await cohort.GroupBy(d => d.CreatedAtUtc.Date)
                .Select(g => new { Day = g.Key, Count = g.Count() }).ToListAsync(cancellationToken);

            var plans = db.DispatchPlans.AsNoTracking().Where(p => p.ConfirmedAtUtc >= period.StartUtc &&
                p.ConfirmedAtUtc < period.EndExclusiveUtc && p.ConfirmedAtUtc <= asOfUtc);
            var routes = plans.SelectMany(p => p.Routes);
            var routeSummary = await routes.GroupBy(_ => 1).Select(g => new
            {
                Routes = g.Count(), Vehicles = g.Select(r => r.VehicleId).Distinct().Count(),
                Meters = g.Sum(r => r.DistanceMeters), Seconds = g.Sum(r => r.DurationSeconds)
            }).SingleOrDefaultAsync(cancellationToken);
            var currentFleet = await db.Vehicles.AsNoTracking().GroupBy(_ => 1)
                .Select(g => new { Total = g.Count(), Available = g.Count(v => v.Availability == VehicleAvailability.Available) })
                .SingleOrDefaultAsync(cancellationToken);
            var activity = routes.GroupBy(r => r.VehicleId).Select(g => new
            {
                VehicleId = g.Key, Routes = (int?)g.Count(),
                Meters = (double?)g.Sum(r => r.DistanceMeters), Seconds = (double?)g.Sum(r => r.DurationSeconds)
            });
            var rows = await (from vehicle in db.Vehicles.AsNoTracking()
                join usage in activity on vehicle.Id equals usage.VehicleId into joined
                from usage in joined.DefaultIfEmpty()
                orderby usage.Routes descending, vehicle.RegistrationNumber
                select new { vehicle.Id, vehicle.RegistrationNumber, vehicle.Availability,
                    Routes = usage.Routes ?? 0, Meters = usage.Meters ?? 0, Seconds = usage.Seconds ?? 0 })
                .Take(50).ToListAsync(cancellationToken);

            return new(metrics, new(currentFleet?.Total ?? 0, currentFleet?.Available ?? 0,
                routeSummary?.Vehicles ?? 0, await plans.CountAsync(cancellationToken), routeSummary?.Routes ?? 0,
                routeSummary?.Meters ?? 0, routeSummary?.Seconds ?? 0),
                daily.Select(row => new DailyIntake(DateOnly.FromDateTime(row.Day), row.Count)).ToArray(),
                rows.Select(row => new VehicleActivity(row.Id, row.RegistrationNumber, row.Availability.ToString(),
                    row.Routes, row.Meters, row.Seconds)).ToArray());
        }
        catch (DbException error)
        {
            throw new AnalyticsUnavailableException("Analytics data could not be read.", error);
        }
    }
}
