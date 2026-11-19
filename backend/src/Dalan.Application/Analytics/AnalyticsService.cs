namespace Dalan.Application.Analytics;

public sealed class AnalyticsService(IAnalyticsRepository repository, TimeProvider clock) : IAnalyticsService
{
    public async Task<AnalyticsReport> GetAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var period = AnalyticsPeriod.Create(from, to);
        var generatedAt = clock.GetUtcNow();
        var snapshot = await repository.ReadAsync(period, generatedAt, cancellationToken);
        var counts = snapshot.DailyIntake.ToDictionary(row => row.Day, row => row.Count);
        var days = Enumerable.Range(0, period.Days)
            .Select(offset => period.From.AddDays(offset))
            .Select(day => new DailyIntake(day, counts.GetValueOrDefault(day))).ToArray();
        return new(period, generatedAt, snapshot.Deliveries, snapshot.Fleet, days, snapshot.Vehicles,
            snapshot.Deliveries.DeliveredWithDeadline == 0 ? null :
                100d * (snapshot.Deliveries.DeliveredWithDeadline - snapshot.Deliveries.LateDelivered) /
                    snapshot.Deliveries.DeliveredWithDeadline,
            snapshot.Fleet.TotalVehicles == 0 ? null :
                100d * snapshot.Fleet.DispatchedVehicles / snapshot.Fleet.TotalVehicles);
    }
}
