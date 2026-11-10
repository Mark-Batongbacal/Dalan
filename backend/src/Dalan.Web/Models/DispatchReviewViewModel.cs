namespace Dalan.Web.Models;

public sealed class DispatchReviewViewModel
{
    public List<DispatchReviewVehicle> Vehicles { get; set; } = [];
    public List<DispatchReviewStop> Stops { get; set; } = [];
    public List<DispatchReviewVersion> Versions { get; set; } = [];

    // Filled from current records for display, never trusted from the form.
    public Dictionary<Guid, string> VehicleNames { get; set; } = [];
    public Dictionary<Guid, string> DeliveryNames { get; set; } = [];

    public string? Error { get; set; }
}

public sealed class DispatchReviewVehicle
{
    public Guid VehicleId { get; set; }
    public double StartLatitude { get; set; }
    public double StartLongitude { get; set; }
}

public sealed class DispatchReviewStop
{
    public Guid DeliveryId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public Guid VehicleId { get; set; }
    public int Order { get; set; }
}

public sealed class DispatchReviewVersion
{
    public Guid DeliveryId { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}