using Dalan.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace Dalan.UnitTests;

public sealed class DriverMobileAccessTests
{
    [Fact]
    public void Ordinary_identity_lockout_is_not_an_explicit_admin_suspension()
    {
        var user = new IdentityUser
        {
            LockoutEnabled = true,
            LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(5)
        };
        Assert.False(DriverMobileAccess.IsSuspended(user));
    }

    [Fact]
    public void Only_far_future_enabled_lockout_represents_a_suspended_mobile_account()
    {
        var user = new IdentityUser
        {
            LockoutEnabled = true,
            LockoutEnd = DriverMobileAccess.SuspensionEndUtc
        };
        Assert.True(DriverMobileAccess.IsSuspended(user));
        user.LockoutEnabled = false;
        Assert.False(DriverMobileAccess.IsSuspended(user));
        user.LockoutEnabled = true;
        user.LockoutEnd = null;
        Assert.False(DriverMobileAccess.IsSuspended(user));
    }
}
