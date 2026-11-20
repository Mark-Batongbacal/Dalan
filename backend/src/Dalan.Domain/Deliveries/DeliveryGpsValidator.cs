namespace Dalan.Domain.Deliveries;

public enum DeliveryGpsFailure
{
    None,
    InvalidPosition,
    MissingOrInaccurateFix,
    StaleFix,
    OutsideDestination
}

/// <summary>
/// Evidence screening only; client-reported coordinates can be spoofed and do
/// not independently establish that the recipient received the package.
/// Check fix time relative to the event time (not server arrival), so Room
/// offline replay can preserve authentic historical evidence.
/// </summary>
public static class DeliveryGpsValidator
{
    private const double EarthRadiusMeters = 6_371_000;

    public static DeliveryGpsFailure Check(
        double destinationLatitude,
        double destinationLongitude,
        double? latitude,
        double? longitude,
        double? accuracyMeters,
        DateTimeOffset? fixCapturedAtUtc,
        DateTimeOffset deliveryEventAtUtc,
        double radiusMeters = 100,
        double maxAccuracyMeters = 30,
        double maxFixAgeSeconds = 120)
    {
        if (latitude is not double lat || longitude is not double lon
            || !double.IsFinite(lat) || !double.IsFinite(lon)
            || lat is < -90 or > 90 || lon is < -180 or > 180)
            return DeliveryGpsFailure.InvalidPosition;

        if (accuracyMeters is not double accuracy || !double.IsFinite(accuracy)
            || accuracy is < 0 or > 10000 || accuracy > maxAccuracyMeters)
            return DeliveryGpsFailure.MissingOrInaccurateFix;

        if (fixCapturedAtUtc is not DateTimeOffset fix || fix == default
            || deliveryEventAtUtc == default
            || fix > deliveryEventAtUtc.AddSeconds(5)
            || (deliveryEventAtUtc - fix).TotalSeconds > maxFixAgeSeconds)
            return DeliveryGpsFailure.StaleFix;

        return DistanceMeters(destinationLatitude, destinationLongitude, lat, lon) <= radiusMeters
            ? DeliveryGpsFailure.None
            : DeliveryGpsFailure.OutsideDestination;
    }

    public static double DistanceMeters(
        double latitude1, double longitude1, double latitude2, double longitude2)
    {
        var latDelta = (latitude2 - latitude1) * Math.PI / 180;
        var lonDelta = (longitude2 - longitude1) * Math.PI / 180;
        var sinLat = Math.Sin(latDelta / 2);
        var sinLon = Math.Sin(lonDelta / 2);
        var a = sinLat * sinLat
            + Math.Cos(latitude1 * Math.PI / 180)
            * Math.Cos(latitude2 * Math.PI / 180)
            * sinLon * sinLon;
        a = Math.Clamp(a, 0, 1);
        return 2 * EarthRadiusMeters * Math.Asin(Math.Sqrt(a));
    }
}
