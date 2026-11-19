namespace Dalan.Application.Analytics;

public interface IAnalyticsRepository
{
    Task<AnalyticsSnapshot> ReadAsync(AnalyticsPeriod period, DateTimeOffset asOfUtc,
        CancellationToken cancellationToken = default);
}
