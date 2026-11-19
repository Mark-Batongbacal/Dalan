using Dalan.Domain.Tracking;
using Xunit;

namespace Dalan.UnitTests;

public sealed class VehicleTrackingTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);
    private static VehicleTrackingSession Session() => VehicleTrackingSession.Start(Guid.NewGuid(), Guid.NewGuid(), Start);
    private static VehicleTrackingSample Fix(VehicleTrackingSession session, int seconds, double latitude = 14, double accuracy = 5)
        => session.Observe(Guid.NewGuid(), latitude, 121, accuracy, Start.AddSeconds(seconds), Start.AddSeconds(seconds), "test");

    [Fact]
    public void Movement_excludes_first_fix_and_large_gaps_but_counts_stationary_coverage()
    {
        var session = Session();
        Assert.Equal(0, Fix(session, 0).ObservedSeconds);
        var stationary = Fix(session, 30);
        Assert.Equal(30, stationary.ObservedSeconds); Assert.False(stationary.Moving);
        var moving = Fix(session, 60, 14.001);
        Assert.True(moving.Moving); Assert.InRange(moving.DistanceMeters, 110, 112);
        Assert.Equal(0, Fix(session, 181, 14.002).ObservedSeconds);
    }

    [Fact]
    public void Noise_below_accuracy_is_not_moving()
    {
        var session = Session(); Fix(session, 0);
        var sample = Fix(session, 30, 14.0001, 30);
        Assert.False(sample.Moving); Assert.Equal(0, sample.DistanceMeters);
    }

    [Theory]
    [InlineData(double.NaN, 5)]
    [InlineData(91, 5)]
    [InlineData(14, 101)]
    [InlineData(14, -1)]
    public void Invalid_points_are_rejected_without_updating_position(double latitude, double accuracy)
    {
        var session = Session();
        Assert.Throws<ArgumentException>(() => Fix(session, 0, latitude, accuracy));
        Assert.Null(session.LastCapturedAtUtc);
    }

    [Fact]
    public void Jumps_stale_future_reordered_and_stopped_samples_are_rejected()
    {
        var session = Session(); Fix(session, 0);
        Assert.Throws<ArgumentException>(() => Fix(session, 30, 15));
        Assert.Throws<ArgumentException>(() => session.Observe(Guid.NewGuid(), 14, 121, 5, Start.AddSeconds(30), Start.AddSeconds(151), "test"));
        Assert.Throws<ArgumentException>(() => session.Observe(Guid.NewGuid(), 14, 121, 5, Start.AddSeconds(30), Start, "test"));
        Assert.Throws<InvalidOperationException>(() => Fix(session, 0));
        session.Stop(Start.AddSeconds(30));
        Assert.Throws<InvalidOperationException>(() => Fix(session, 60));
    }
}
