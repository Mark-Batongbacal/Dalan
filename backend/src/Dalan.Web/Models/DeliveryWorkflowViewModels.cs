using System.ComponentModel.DataAnnotations;
using Dalan.Application.Deliveries;
using Dalan.Application.Drivers;
using Dalan.Application.Vehicles;
using Dalan.Contracts.Dispatch;

namespace Dalan.Web.Models;

public sealed class WarehousePreferenceViewModel
{
    public bool ReturnToDispatch { get; set; }
    [Required, StringLength(200)] public string Name { get; set; } = "";
    [Required, Range(-90d,90d)] public double? Latitude { get; set; }
    [Required, Range(-180d,180d)] public double? Longitude { get; set; }
    public string? SearchAddress { get; set; }
}
public sealed record DeliveryWorkflowViewModel(WarehousePreference? Warehouse, int Drivers, int Vehicles, int Deliveries, int Assigned);
public sealed record DeliveryAssignmentsViewModel(IReadOnlyList<DeliveryDetails> Deliveries, IReadOnlyList<DriverDetails> Drivers, IReadOnlyList<VehicleDetails> Vehicles);
public sealed class DeliveryBatchViewModel
{
    public List<DeliveryBatchRow> Rows { get; set; } = [new()];
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever] public WarehousePreference? Warehouse { get; set; }
    public DateTimeOffset? ExpectedDeliveryAtUtc { get; set; }
    public string? ExpectedDeliveryLocal { get; set; }
}
public sealed class DeliveryBatchRow
{
    [Required, StringLength(120)] public string RecipientName { get; set; } = "";
    [Required, StringLength(500)] public string DropoffAddress { get; set; } = "";
    [Required, Range(-90d,90d)] public double? DropoffLatitude { get; set; }
    [Required, Range(-180d,180d)] public double? DropoffLongitude { get; set; }
    [Range(0.01,double.MaxValue)] public decimal? PayloadWeightKg { get; set; }
}
