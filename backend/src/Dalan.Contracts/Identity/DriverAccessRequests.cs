using System.ComponentModel.DataAnnotations;
namespace Dalan.Contracts.Identity;

public sealed class DriverAccessChangeRequest
{
    [Required, StringLength(240, MinimumLength = 8)]
    public string Reason { get; init; } = "";
}
