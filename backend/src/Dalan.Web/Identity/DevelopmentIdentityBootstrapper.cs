using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Dalan.Web.Identity;

public static class DevelopmentIdentityBootstrapper
{
    public static async Task BootstrapAsync(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return;
        }

        using var scope = app.Services.CreateScope();

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

        foreach (var roleName in new[] { IdentityRoles.Admin, IdentityRoles.Dispatcher })
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                EnsureSucceeded(await roleManager.CreateAsync(new IdentityRole(roleName)));
            }
        }

        var adminEmail = app.Configuration["Identity:Admin:Email"];
        var adminPassword = app.Configuration["Identity:Admin:Password"];
        var dispatcherEmail = app.Configuration["Identity:Dispatcher:Email"];
        var dispatcherPassword = app.Configuration["Identity:Dispatcher:Password"];

        if (!string.IsNullOrWhiteSpace(adminEmail) &&
            string.Equals(adminEmail, dispatcherEmail, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Development Admin and Dispatcher accounts must use different email addresses.");
        }

        await EnsureDevelopmentUserAsync(
            app,
            userManager,
            IdentityRoles.Admin,
            adminEmail,
            adminPassword);

        await EnsureDevelopmentUserAsync(
            app,
            userManager,
            IdentityRoles.Dispatcher,
            dispatcherEmail,
            dispatcherPassword);
    }

    private static async Task EnsureDevelopmentUserAsync(
        WebApplication app,
        UserManager<IdentityUser> userManager,
        string roleName,
        string? email,
        string? password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            app.Logger.LogWarning(
                "Development {Role} bootstrap skipped. Configure its email and password through user secrets.",
                roleName);

            return;
        }

        var user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            user = new IdentityUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true
            };

            EnsureSucceeded(await userManager.CreateAsync(user, password));
        }

        if (!await userManager.IsInRoleAsync(user, roleName))
        {
            EnsureSucceeded(await userManager.AddToRoleAsync(user, roleName));
        }
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (result.Succeeded)
        {
            return;
        }

        var errors = string.Join(
            "; ",
            result.Errors.Select(error => error.Description));

        throw new InvalidOperationException(errors);
    }
}