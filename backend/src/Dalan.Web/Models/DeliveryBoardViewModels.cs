using System.ComponentModel.DataAnnotations;
using Dalan.Application.Deliveries;
using Dalan.Application.Drivers;
using Dalan.Application.Vehicles;

namespace Dalan.Web.Models;

public sealed class CreateDeliveryViewModel
{
    [Required, StringLength(120)]
    public string RecipientName { get; set; } = string.Empty;

    [Required, StringLength(500)]
    public string PickupAddress { get; set; } = string.Empty;

    [Required, Range(-90d, 90d)]
    public double? PickupLatitude { get; set; }

    [Required, Range(-180d, 180d)]
    public double? PickupLongitude { get; set; }

    [Required, StringLength(500)]
    public string DropoffAddress { get; set; } = string.Empty;

    [Required, Range(-90d, 90d)]
    public double? DropoffLatitude { get; set; }

    [Required, Range(-180d, 180d)]
    public double? DropoffLongitude { get; set; }

    [Range(0.01, double.MaxValue)]
    public decimal? PayloadWeightKg { get; set; }

    [Display(Name = "Expected delivery time (UTC)")]
    public DateTimeOffset? ExpectedDeliveryAtUtc { get; set; }
}

public sealed record DeliveryDetailsViewModel(
    DeliveryDetails Delivery,
    IReadOnlyList<DriverDetails> Drivers,
    IReadOnlyList<VehicleDetails> Vehicles);
public sealed record DispatchDriverRow(
    string Name,
    string Availability,
    DateOnly LicenseExpiry,
    string? VehicleRegistration);

public sealed record DispatchDriverRosterViewModel(
    IReadOnlyList<DispatchDriverRow> Drivers);