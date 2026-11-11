using System.Text.Json;
using Dalan.Domain.Deliveries;
using Dalan.Domain.Vehicles;
using Dalan.Infrastructure.Persistence;
using Hangfire.Server;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dalan.Worker;

public sealed class OperationsJobs(
    DalanDbContext db, TimeProvider clock, ILogger<OperationsJobs> logger)
{
    public async Task RunAsync(string kind, PerformContext? context, CancellationToken cancellationToken)
    {
        var now = context is null ? clock.GetUtcNow() : new DateTimeOffset(context.BackgroundJob.CreatedAt, TimeSpan.Zero);
        // Each snapshot represents a fixed interval, even if multiple workers run it.
        var hour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
        var day = DateOnly.FromDateTime(now.UtcDateTime);
        var key = kind == "overdue" ? $"overdue-{hour.Hour:D2}" : kind;
        if (await Exists()) return;

        var dayStart = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        object data = kind switch
        {
            "overdue" => await db.Deliveries
                .Where(x => x.ExpectedDeliveryAtUtc < hour && x.CreatedAtUtc < hour &&
                    (x.DeliveredAtUtc == null || x.DeliveredAtUtc >= hour) &&
                    (x.CancelledAtUtc == null || x.CancelledAtUtc >= hour))
                .OrderBy(x => x.ExpectedDeliveryAtUtc).ThenBy(x => x.Id)
                .Select(x => new { x.Id, x.ExpectedDeliveryAtUtc }).ToListAsync(cancellationToken),
            "dispatch" => await db.DispatchPlans
                .Where(x => x.ConfirmedAtUtc >= dayStart.AddDays(-1) && x.ConfirmedAtUtc < dayStart)
                .OrderBy(x => x.ConfirmedAtUtc).ThenBy(x => x.Id)
                .Select(x => new { x.Id, x.ConfirmedAtUtc, Routes = x.Routes.Count,
                    Deliveries = x.Routes.SelectMany(route => route.Stops).Count() / 2 })
                .ToListAsync(cancellationToken),
            "maintenance" => await db.Vehicles
                .Where(x => x.Availability != VehicleAvailability.Inactive && x.NextMaintenanceDate <= day.AddDays(7))
                .OrderBy(x => x.NextMaintenanceDate).ThenBy(x => x.Id)
                .Select(x => new { x.Id, x.RegistrationNumber, x.NextMaintenanceDate })
                .ToListAsync(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        db.OperationsSnapshots.Add(new OperationsSnapshot
        {
            DayUtc = day, Kind = key, Data = JsonSerializer.Serialize(data)
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Saved operations snapshot {Kind} for {Day}", key, day);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            if (!await Exists()) throw;
        }

        Task<bool> Exists() => db.OperationsSnapshots.AnyAsync(
            x => x.DayUtc == day && x.Kind == key, cancellationToken);
    }
}
