namespace Dalan.Application.Analytics;

public interface IAnalyticsService
{
    Task<AnalyticsReport> GetAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
