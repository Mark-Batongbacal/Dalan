using Dalan.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Dalan.Web.Controllers;

[Authorize(Roles = "Admin")]
public sealed class StaffAccountsController(UserManager<IdentityUser> users) : Controller
{
    [HttpGet]
    public IActionResult Create() => View(new CreateStaffAccountViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateStaffAccountViewModel model)
    {
        if (model.Role is not ("Admin" or "Dispatcher"))
            ModelState.AddModelError(nameof(model.Role), "Choose a valid staff role.");
        if (!ModelState.IsValid) return View(model);

        var user = new IdentityUser
        {
            UserName = model.Email.Trim(),
            Email = model.Email.Trim(),
            EmailConfirmed = true
        };
        var created = await users.CreateAsync(user, model.Password);
        if (!created.Succeeded)
        {
            foreach (var error in created.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        var assigned = await users.AddToRoleAsync(user, model.Role);
        if (!assigned.Succeeded)
        {
            await users.DeleteAsync(user);
            foreach (var error in assigned.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        TempData["AccountCreated"] = "Staff account created.";
        return RedirectToAction(nameof(Create));
    }
}
