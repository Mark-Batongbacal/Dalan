using Dalan.Application.Analytics;
using Xunit;

namespace Dalan.UnitTests;

public sealed class AnalyticsServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Validates_period_before_repository_access()
    {
        var repository = new Repository();
        var service = new AnalyticsService(repository, new Clock());
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetAsync(new(2026, 11, 18), new(2026, 11, 17)));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetAsync(new(2025, 1, 1), new(2026, 1, 2)));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetAsync(new(9999, 12, 30), DateOnly.MaxValue));
        Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task Fills_zero_intake_days_and_passes_one_clock_and_cancellation()
    {
        var repository = new Repository
        {
            Snapshot = new(new(5, 1, 1, 0, 3, 0, 1, 1, 2, 600), new(4, 3, 2, 2, 3, 10000, 900),
                [new(new(2026, 11, 16), 5)], [])
        };
        using var cancellation = new CancellationTokenSource();
        var report = await new AnalyticsService(repository, new Clock()).GetAsync(new(2026, 11, 15), new(2026, 11, 17), cancellation.Token);
        Assert.Equal(new[] { 0, 5, 0 }, report.DailyIntake.Select(x => x.Count));
        Assert.Equal(50d, report.OnTimePercent);
        Assert.Equal(50d, report.FleetParticipationPercent);
        Assert.Equal(Now, report.GeneratedAtUtc);
        Assert.Equal(Now, repository.AsOf);
        Assert.Equal(cancellation.Token, repository.Token);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 0, 0, 0, TimeSpan.Zero), repository.Period!.StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero), repository.Period.EndExclusiveUtc);
    }

    [Fact]
    public async Task Missing_denominators_and_no_completed_data_remain_unavailable()
    {
        var report = await new AnalyticsService(new Repository(), new Clock()).GetAsync(new(2026, 11, 17), new(2026, 11, 17));
        Assert.Null(report.OnTimePercent);
        Assert.Null(report.FleetParticipationPercent);
        Assert.Null(report.Deliveries.AverageFulfilmentSeconds);
        Assert.Equal(0, Assert.Single(report.DailyIntake).Count);
    }

    [Fact]
    public void Inclusive_leap_year_range_allows_366_days_but_not_367()
    {
        Assert.Equal(366, AnalyticsPeriod.Create(new(2024, 1, 1), new(2024, 12, 31)).Days);
        Assert.Throws<ArgumentException>(() => AnalyticsPeriod.Create(new(2024, 1, 1), new(2025, 1, 1)));
    }

    [Fact]
    public async Task Storage_failure_is_not_disguised_as_zero_metrics()
    {
        var repository = new Repository { Fail = true };
        await Assert.ThrowsAsync<AnalyticsUnavailableException>(() =>
            new AnalyticsService(repository, new Clock()).GetAsync(new(2026, 11, 17), new(2026, 11, 17)));
    }

    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Repository : IAnalyticsRepository
    {
        public AnalyticsSnapshot Snapshot { get; init; } = new(new(0, 0, 0, 0, 0, 0, 0, 0, 0, null), new(0, 0, 0, 0, 0, 0, 0), [], []);
        public int Calls { get; private set; }
        public bool Fail { get; init; }
        public AnalyticsPeriod? Period { get; private set; }
        public DateTimeOffset AsOf { get; private set; }
        public CancellationToken Token { get; private set; }
        public Task<AnalyticsSnapshot> ReadAsync(AnalyticsPeriod period, DateTimeOffset asOfUtc, CancellationToken token)
        {
            Calls++; Period = period; AsOf = asOfUtc; Token = token;
            if (Fail) throw new AnalyticsUnavailableException("Unavailable", new Exception());
            return Task.FromResult(Snapshot);
        }
    }
}
