using Microsoft.AspNetCore.Identity;

namespace Dalan.Infrastructure.Identity;

/// <summary>
/// Driver mobile access is distinct from the Driver's fleet availability.
/// Use Identity's existing LockoutEnd column for a durable, explicitly managed
/// suspension without a schema migration. Temporary bad-password lockouts
/// must not be mistaken for an administrator suspension.
/// </summary>
public static class DriverMobileAccess
{
    public static readonly DateTimeOffset SuspensionEndUtc =
        new(9999, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static bool IsSuspended(IdentityUser user) =>
        user.LockoutEnabled && user.LockoutEnd >= SuspensionEndUtc;
}
