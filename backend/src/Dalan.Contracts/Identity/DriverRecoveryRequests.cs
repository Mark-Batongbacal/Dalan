using System.ComponentModel.DataAnnotations;
namespace Dalan.Contracts.Identity;

public sealed class DriverRecoveryEmailRequest
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; init; } = "";
}

public sealed class DriverRecoveryPasswordRequest
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; init; } = "";

    [Required, StringLength(4096)]
    public string Token { get; init; } = "";

    [Required, StringLength(128, MinimumLength = 12)]
    public string Password { get; init; } = "";
}
