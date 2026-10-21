using Dalan.Contracts.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Dalan.InternalApi.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class StaffAuthController(
    UserManager<IdentityUser> users,
    SignInManager<IdentityUser> signInManager) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(StaffLoginRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email!);
        if (user is null || !await users.IsInRoleAsync(user, "Admin")
            && !await users.IsInRoleAsync(user, "Dispatcher"))
            return Unauthorized();

        var result = await signInManager.CheckPasswordSignInAsync(
            user, request.Password!, lockoutOnFailure: true);
        if (!result.Succeeded) return Unauthorized();

        var principal = await signInManager.CreateUserPrincipalAsync(user);
        return SignIn(principal, IdentityConstants.BearerScheme);
    }
}
