namespace Dalan.Application.Tracking;

public sealed class TrackingOptions { public bool Enabled { get; set; } }
public sealed record TrackingFix(Guid SampleId, DateTimeOffset CapturedAtUtc, double Latitude, double Longitude, double AccuracyMeters);
public sealed record TrackingStart(Guid SessionId, Guid VehicleId);
public enum TrackingWriteStatus { Accepted, Duplicate, Missing, Conflict, Invalid, TooFrequent, Stopped }
public sealed record VehiclePosition(Guid VehicleId, string RegistrationNumber, string? DriverName,
    double? Latitude, double? Longitude, double? AccuracyMeters, DateTimeOffset? CapturedAtUtc, string Status);
public sealed record ObservedFleetActivity(double ObservedSeconds, double MovingSeconds, double GpsDistanceMeters, int Samples);
public sealed record FleetTrackingReport(bool Enabled, DateOnly From, DateOnly To, DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<VehiclePosition> Vehicles, ObservedFleetActivity Activity, double? MovementUtilizationPercent);
public sealed record FleetPositions(bool Enabled, DateTimeOffset GeneratedAtUtc, IReadOnlyList<VehiclePosition> Vehicles);

public interface IFleetTrackingRepository
{
    Task<TrackingStart?> StartAsync(Guid driverId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<TrackingWriteStatus> RecordAsync(Guid driverId, Guid sessionId, TrackingFix fix, DateTimeOffset now, CancellationToken cancellationToken);
    Task StopAsync(Guid driverId, Guid sessionId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<IReadOnlyList<VehiclePosition>> PositionsAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<ObservedFleetActivity> ActivityAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken);
    Task CleanupAsync(DateTimeOffset now, CancellationToken cancellationToken);
}
public interface IFleetTrackingService
{
    Task<TrackingStart?> StartAsync(Guid driverId, CancellationToken cancellationToken);
    Task<TrackingWriteStatus> RecordAsync(Guid driverId, Guid sessionId, TrackingFix fix, CancellationToken cancellationToken);
    Task StopAsync(Guid driverId, Guid sessionId, CancellationToken cancellationToken);
    Task<FleetTrackingReport> ReportAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
    Task<FleetPositions> PositionsAsync(CancellationToken cancellationToken);
    Task CleanupAsync(CancellationToken cancellationToken);
}
public sealed class FleetTrackingService(IFleetTrackingRepository repository, TrackingOptions options, TimeProvider clock) : IFleetTrackingService
{
    public async Task<FleetPositions> PositionsAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        return new(options.Enabled, now, options.Enabled ? await repository.PositionsAsync(now, cancellationToken) : []);
    }
    public Task<TrackingStart?> StartAsync(Guid driverId, CancellationToken cancellationToken) => options.Enabled
        ? repository.StartAsync(driverId, clock.GetUtcNow(), cancellationToken) : Task.FromResult<TrackingStart?>(null);
    public Task<TrackingWriteStatus> RecordAsync(Guid driverId, Guid sessionId, TrackingFix fix, CancellationToken cancellationToken) => options.Enabled
        ? repository.RecordAsync(driverId, sessionId, fix, clock.GetUtcNow(), cancellationToken) : Task.FromResult(TrackingWriteStatus.Stopped);
    public Task StopAsync(Guid driverId, Guid sessionId, CancellationToken cancellationToken) => options.Enabled
        ? repository.StopAsync(driverId, sessionId, clock.GetUtcNow(), cancellationToken) : Task.CompletedTask;

    public async Task<FleetTrackingReport> ReportAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow(); var today = DateOnly.FromDateTime(now.UtcDateTime);
        if (from > to || from < today.AddDays(-29) || to > today) throw new ArgumentException("Choose UTC dates within the last 30 days, with start on or before end.");
        if (!options.Enabled) return new(false, from, to, now, [], new(0, 0, 0, 0), null);
        var positions = await repository.PositionsAsync(now, cancellationToken);
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var activity = await repository.ActivityAsync(start, end > now ? now : end, cancellationToken);
        return new(true, from, to, now, positions, activity, activity.ObservedSeconds > 0 ? 100 * activity.MovingSeconds / activity.ObservedSeconds : null);
    }

    public Task CleanupAsync(CancellationToken cancellationToken) => options.Enabled
        ? repository.CleanupAsync(clock.GetUtcNow(), cancellationToken) : Task.CompletedTask;
}
