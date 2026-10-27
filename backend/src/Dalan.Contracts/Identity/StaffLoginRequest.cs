using System.ComponentModel.DataAnnotations;

namespace Dalan.Contracts.Identity;

public sealed record StaffLoginRequest([Required, EmailAddress] string? Email, [Required] string? Password);
