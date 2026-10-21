using System.ComponentModel.DataAnnotations;

namespace Dalan.Web.Models;

public sealed class CreateStaffAccountViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(12)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = "Dispatcher";
}
