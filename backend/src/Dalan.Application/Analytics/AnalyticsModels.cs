namespace Dalan.Application.Analytics;

public sealed record AnalyticsPeriod
{
    private AnalyticsPeriod(DateOnly from, DateOnly to) { From = from; To = to; }
    public DateOnly From { get; }
    public DateOnly To { get; }
    public int Days => To.DayNumber - From.DayNumber + 1;
    public DateTimeOffset StartUtc => new(From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
    public DateTimeOffset EndExclusiveUtc => new(To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

    public static AnalyticsPeriod Create(DateOnly from, DateOnly to)
    {
        if (from > to) throw new ArgumentException("The start date must be on or before the end date.");
        if (to == DateOnly.MaxValue) throw new ArgumentException("Choose an end date before 9999-12-31.");
        if (to.DayNumber - from.DayNumber >= 366) throw new ArgumentException("Choose a period of at most 366 days.");
        return new(from, to);
    }
}

public sealed record DeliveryAnalytics(int Total, int Pending, int Assigned, int PickedUp,
    int Delivered, int Cancelled, int Overdue, int LateDelivered, int DeliveredWithDeadline,
    double? AverageFulfilmentSeconds)
{
    public int Open => Pending + Assigned + PickedUp;
}
public sealed record FleetAnalytics(int TotalVehicles, int AvailableVehicles, int DispatchedVehicles,
    int ConfirmedPlans, int ConfirmedRoutes, double PlannedDistanceMeters, double PlannedDrivingSeconds);
public sealed record DailyIntake(DateOnly Day, int Count);
public sealed record VehicleActivity(Guid VehicleId, string RegistrationNumber, string Availability,
    int ConfirmedRoutes, double PlannedDistanceMeters, double PlannedDrivingSeconds);
public sealed record AnalyticsSnapshot(DeliveryAnalytics Deliveries, FleetAnalytics Fleet,
    IReadOnlyList<DailyIntake> DailyIntake, IReadOnlyList<VehicleActivity> Vehicles);
public sealed record AnalyticsReport(AnalyticsPeriod Period, DateTimeOffset GeneratedAtUtc,
    DeliveryAnalytics Deliveries, FleetAnalytics Fleet, IReadOnlyList<DailyIntake> DailyIntake,
    IReadOnlyList<VehicleActivity> Vehicles, double? OnTimePercent, double? FleetParticipationPercent);

public sealed class AnalyticsUnavailableException(string message, Exception innerException)
    : Exception(message, innerException);
