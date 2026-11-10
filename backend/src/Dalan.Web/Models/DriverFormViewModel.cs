using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Dalan.Web.Models;

public sealed class DriverFormViewModel
{
    public Guid Id { get; set; }

    [Required, StringLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(32)]
    public string Phone { get; set; } = string.Empty;

    [Required, StringLength(64)]
    [Display(Name = "License number")]
    public string LicenseNumber { get; set; } = string.Empty;

    [Required, DataType(DataType.Date)]
    [Display(Name = "License expiry")]
    public DateOnly? LicenseExpiry { get; set; }

    public string Availability { get; set; } = "Available";

    [Display(Name = "Assigned vehicle")]
    public Guid? AssignedVehicleId { get; set; }

    public IReadOnlyList<SelectListItem> Vehicles { get; set; } = [];
}

public sealed record DriverListItemViewModel(
    Guid Id, string Name, string Phone, string LicenseNumber,
    DateOnly LicenseExpiry, string Availability, string? VehicleRegistration);

public sealed record DriverRegistryViewModel(IReadOnlyList<DriverListItemViewModel> Drivers);
