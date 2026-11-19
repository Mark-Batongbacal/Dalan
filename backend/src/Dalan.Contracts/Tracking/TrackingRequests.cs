namespace Dalan.Contracts.Tracking;

public sealed record TrackingFixRequest(Guid SampleId, DateTimeOffset CapturedAtUtc,
    double Latitude, double Longitude, double AccuracyMeters);
public sealed record TrackingSessionResponse(Guid SessionId, Guid VehicleId);
