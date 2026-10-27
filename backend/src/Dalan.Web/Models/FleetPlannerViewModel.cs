using Dalan.Application.FleetOptimization;

namespace Dalan.Web.Models;

public sealed class FleetPlannerViewModel
{
    public List<PlannerVehicleInput> Vehicles { get; set; } = [];
    public List<PlannerJobInput> Jobs { get; set; } = [new()];
    public FleetOptimizationResult? Result { get; set; }
    public string? Error { get; set; }
}

public sealed class PlannerVehicleInput
{
    public Guid Id { get; set; }
    public string RegistrationNumber { get; set; } = string.Empty;
    public bool Selected { get; set; }
    public double? StartLatitude { get; set; }
    public double? StartLongitude { get; set; }
}

public sealed class PlannerJobInput
{
    public string? Id { get; set; }
    public double? PickupLatitude { get; set; }
    public double? PickupLongitude { get; set; }
    public double? DropoffLatitude { get; set; }
    public double? DropoffLongitude { get; set; }
    public Guid? AssignedVehicleId { get; set; }
}
