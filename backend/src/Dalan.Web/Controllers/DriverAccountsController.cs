using System.ComponentModel.DataAnnotations;
using Dalan.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Dalan.Web.Controllers;

[Authorize(Roles = "Admin")]
public sealed class DriverAccountsController(
    DalanDbContext db, UserManager<IdentityUser> users, RoleManager<IdentityRole> roles) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Create(Guid id, CancellationToken cancellationToken)
    {
        var driver = await db.Drivers.FindAsync([id], cancellationToken);
        if (driver is null) return NotFound();
        var linked = await db.DriverAccounts.AnyAsync(x => x.DriverId == id, cancellationToken);
        if (linked) return View("Linked", driver.Name);
        return View(new DriverAccountForm { DriverId = id, DriverName = driver.Name });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DriverAccountForm model, CancellationToken cancellationToken)
    {
        var driver = await db.Drivers.FindAsync([model.DriverId], cancellationToken);
        if (driver is null) return NotFound();
        model.DriverName = driver.Name;
        if (!ModelState.IsValid) return View(model);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await db.DriverAccounts.AnyAsync(x => x.DriverId == driver.Id, cancellationToken))
            return View("Linked", driver.Name);

        try
        {
            if (!await roles.RoleExistsAsync("Driver"))
            {
                var role = await roles.CreateAsync(new IdentityRole("Driver"));
                if (!role.Succeeded) return WithErrors(model, role);
            }

            var email = model.Email.Trim();
            var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            var created = await users.CreateAsync(user, model.Password);
            if (!created.Succeeded) return WithErrors(model, created);
            var assigned = await users.AddToRoleAsync(user, "Driver");
            if (!assigned.Succeeded) return WithErrors(model, assigned);
            db.DriverAccounts.Add(new DriverAccount { DriverId = driver.Id, UserId = user.Id });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            ModelState.AddModelError(string.Empty, "An account was created concurrently. Reload and try again.");
            return View(model);
        }
        return RedirectToAction(nameof(Create), new { id = driver.Id });
    }

    private IActionResult WithErrors(DriverAccountForm model, IdentityResult result)
    {
        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);
        return View("Create", model);
    }
}

public sealed class DriverAccountForm
{
    public Guid DriverId { get; set; }
    public string? DriverName { get; set; }

    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = "";

    [Required, StringLength(128, MinimumLength = 12), DataType(DataType.Password)]
    public string Password { get; set; } = "";
}
