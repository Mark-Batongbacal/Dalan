using Dalan.Domain.Deliveries;
using Xunit;

namespace Dalan.UnitTests;

public sealed class DeliveryGpsValidatorTests
{
    private static readonly DateTimeOffset ConfirmedAt = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Same_point_with_recent_accurate_fix_passes()
    {
        Assert.Equal(DeliveryGpsFailure.None, Check(14.6, 121, 14.6, 121, 8, ConfirmedAt.AddSeconds(-25)));
    }

    [Fact]
    public void Slightly_away_within_100_meters_passes_but_100_plus_does_not()
    {
        Assert.Equal(DeliveryGpsFailure.None,
            Check(14.6, 121, 14.6005, 121, 10, ConfirmedAt));
        Assert.Equal(DeliveryGpsFailure.OutsideDestination,
            Check(14.6, 121, 14.6011, 121, 10, ConfirmedAt));
        Assert.InRange(DeliveryGpsValidator.DistanceMeters(14.6, 121, 14.6011, 121), 120, 124);
    }

    [Fact]
    public void Missing_inaccurate_and_stale_fix_cannot_verify_delivery()
    {
        Assert.Equal(DeliveryGpsFailure.MissingOrInaccurateFix,
            Check(14.6, 121, 14.6, 121, null, ConfirmedAt));
        Assert.Equal(DeliveryGpsFailure.MissingOrInaccurateFix,
            Check(14.6, 121, 14.6, 121, 45, ConfirmedAt));
        Assert.Equal(DeliveryGpsFailure.StaleFix,
            Check(14.6, 121, 14.6, 121, 5, ConfirmedAt.AddMinutes(-4)));
        Assert.Equal(DeliveryGpsFailure.StaleFix,
            Check(14.6, 121, 14.6, 121, 5, ConfirmedAt.AddSeconds(30)));
        Assert.Equal(DeliveryGpsFailure.None,
            Check(14.6, 121, 14.6, 121, 30, ConfirmedAt.AddMinutes(-2)));
    }

    [Fact]
    public void Invalid_coordinates_cannot_be_substituted_for_evidence()
    {
        Assert.Equal(DeliveryGpsFailure.InvalidPosition,
            Check(14.6, 121, 91, 121, 5, ConfirmedAt));
        Assert.Equal(DeliveryGpsFailure.InvalidPosition,
            Check(14.6, 121, double.NaN, 121, 5, ConfirmedAt));
    }

    private static DeliveryGpsFailure Check(
        double destLat, double destLon, double? lat, double? lon, double? accuracy,
        DateTimeOffset? fixTime) =>
        DeliveryGpsValidator.Check(destLat, destLon, lat, lon, accuracy, fixTime, ConfirmedAt);
}
