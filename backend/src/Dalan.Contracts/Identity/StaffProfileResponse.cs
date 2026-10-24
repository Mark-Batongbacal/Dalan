namespace Dalan.Contracts.Identity;

public sealed record StaffProfileResponse(
    string Id,
    string Email,
    IReadOnlyList<string> Roles);