using System.ComponentModel.DataAnnotations;
using Dalan.Application.Identity;
using Dalan.Contracts.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Dalan.Web.Controllers;
[Authorize(Roles = "Admin")]
public sealed class DriverAccountsController(IDriverAccountsService accounts, IConfiguration configuration) : Controller
{
    private bool Invitations => bool.TryParse(configuration["DriverInvitations:Enabled"], out var enabled) && enabled;
    [HttpGet]
    public async Task<IActionResult> Create(Guid id, CancellationToken cancellationToken)
    {
        if (Invitations) return Redirect($"/Drivers/{id}/Access");
        var driver = await accounts.Find(id, cancellationToken);
        if (driver is null) return NotFound();
        return driver.Linked ? View("Linked", driver.Name) : View(new DriverAccountForm { DriverId = id, DriverName = driver.Name });
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DriverAccountForm model, CancellationToken cancellationToken)
    {
        if (Invitations) return StatusCode(410, "Use the Driver invitation workflow. Administrators do not set Driver passwords.");
        var driver = await accounts.Find(model.DriverId, cancellationToken);
        if (driver is null) return NotFound(); model.DriverName = driver.Name;
        if (!ModelState.IsValid) return View(model);
        var result = await accounts.Create(new(model.DriverId, model.Email, model.Password), cancellationToken);
        if (result.StatusCode == 201) return RedirectToAction(nameof(Create), new { id = model.DriverId });
        if (result.StatusCode == 404) return NotFound();
        if (result.StatusCode == 409) return View("Linked", driver.Name);
        foreach (var error in result.Value as string[] ?? ["Unable to create this Driver account."]) ModelState.AddModelError("", error);
        return View(model);
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
