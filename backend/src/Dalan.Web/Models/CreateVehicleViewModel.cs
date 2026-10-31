using System.ComponentModel.DataAnnotations;

namespace Dalan.Web.Models;

public sealed class CreateVehicleViewModel
{
    [Required, StringLength(32)]
    [Display(Name = "Registration number")]
    public string RegistrationNumber { get; set; } = string.Empty;

    [StringLength(64)]
    [Display(Name = "Vehicle type")]
    public string? Type { get; set; }

    [Range(0.01, 1000000)]
    [Display(Name = "Payload capacity (kg)")]
    public decimal? PayloadCapacityKg { get; set; }

    [Range(1, 1000)]
    [Display(Name = "Seat capacity")]
    public int? SeatCapacity { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Next maintenance date")]
    public DateOnly? NextMaintenanceDate { get; set; }
}
