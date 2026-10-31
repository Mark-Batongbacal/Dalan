using System.ComponentModel.DataAnnotations;

namespace Dalan.Web.Models;

public sealed class EditVehicleViewModel
{
    public Guid Id { get; set; }
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

    [Required]
    public string Availability { get; set; } = "Available";

    [DataType(DataType.Date)]
    [Display(Name = "Next maintenance date")]
    public DateOnly? NextMaintenanceDate { get; set; }
}
