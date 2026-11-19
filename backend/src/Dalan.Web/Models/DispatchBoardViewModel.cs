using Dalan.Application.Deliveries;
using Dalan.Application.Dispatch;
using Dalan.Application.Vehicles;
using Dalan.Contracts.Dispatch;

namespace Dalan.Web.Models;

public sealed class DispatchBoardViewModel
{
    public DispatchStrategy Strategy { get; set; } = DispatchStrategy.MinimumTotalTravelTime;
    public DispatchStrategyInput Settings { get; set; } = new();
    public DispatchStrategyAdvice? Advice { get; set; }
    public List<DispatchVehicleInput> Vehicles { get; set; } = [];
    public List<DispatchDeliveryInput> Deliveries { get; set; } = [];
    public DispatchPreviewResult? Preview { get; set; }
    public string? Error { get; set; }
}

public sealed class DispatchVehicleInput
{
    public Guid Id { get; set; }
    public string RegistrationNumber { get; set; } = string.Empty;
    public bool Selected { get; set; }
    public double? StartLatitude { get; set; }
    public double? StartLongitude { get; set; }
    [System.ComponentModel.DataAnnotations.Range(0, double.MaxValue)]
    public double ExistingWorkloadSeconds { get; set; }
}

public sealed class DispatchDeliveryInput
{
    public Guid Id { get; set; }
    public string RecipientName { get; set; } = string.Empty;
    public string PickupAddress { get; set; } = string.Empty;
    public string DropoffAddress { get; set; } = string.Empty;
    public double PickupLatitude { get; set; }
    public double PickupLongitude { get; set; }
    public double DropoffLatitude { get; set; }
    public double DropoffLongitude { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool Selected { get; set; }
}
