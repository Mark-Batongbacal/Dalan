using System.ComponentModel.DataAnnotations;

namespace Dalan.Web.Models;

public sealed class CreateVehicleViewModel
{
    [Required, StringLength(32)]
    [Display(Name = "Registration number")]
    public string RegistrationNumber { get; set; } = string.Empty;
}
