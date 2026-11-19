using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using Dalan.Infrastructure.Identity;
using Dalan.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dalan.Web.Controllers;

/// <summary>
/// Admin-only mobile access controls. Changing mobile access never deactivates
/// the Driver, unassigns deliveries, or deletes proof and offline history.
/// Codex owns the eventual UI; these routes require an antiforgery header.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/driver-mobile-access")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DriverAccessController(
    DalanDbContext db,
    UserManager<IdentityUser> users,
    TimeProvider clock,
    ILogger<DriverAccessController> logger) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Status(Guid id, CancellationToken cancellationToken)
    {
        if (!await db.Drivers.AnyAsync(x => x.Id == id, cancellationToken))
            return NotFound();

        var account = await db.DriverAccounts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.DriverId == id, cancellationToken);
        if (account is null)
            return Ok(new { driverId = id, mobileAccess = "NoAccount" });

        var user = await users.FindByIdAsync(account.UserId);
        if (user is null || !await users.IsInRoleAsync(user, "Driver"))
            return Conflict(new { message = "The linked Driver Identity account is invalid." });

        var status = DriverMobileAccess.IsSuspended(user)
            ? "Disabled"
            : await users.HasPasswordAsync(user) ? "Enabled" : "PendingActivation";
        return Ok(new { driverId = id, mobileAccess = status });
    }

    [HttpPost("{id:guid}/disable")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Disable(
        Guid id, DriverAccessChangeRequest request, CancellationToken cancellationToken) =>
        Change(id, request, disable: true, cancellationToken);

    [HttpPost("{id:guid}/enable")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Enable(
        Guid id, DriverAccessChangeRequest request, CancellationToken cancellationToken) =>
        Change(id, request, disable: false, cancellationToken);

    private async Task<IActionResult> Change(
        Guid id, DriverAccessChangeRequest request, bool disable, CancellationToken cancellationToken)
    {
        // Both the reason and the authenticated Admin are included in a structured
        // server log. DLN-35 separately adds persistent read-only audit history.
        var reason = string.Join(" ", request.Reason
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (reason.Length is < 8 or > 240)
            return BadRequest(new { message = "Provide a reason between 8 and 240 characters." });

        await using var transaction =
            await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var account = await db.DriverAccounts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.DriverId == id, cancellationToken);
        if (account is null) return NotFound();

        var user = await users.FindByIdAsync(account.UserId);
        if (user is null || !await users.IsInRoleAsync(user, "Driver"))
            return Conflict(new { message = "The linked Driver Identity account is invalid." });

        if (DriverMobileAccess.IsSuspended(user) == disable)
            return Ok(new { driverId = id, mobileAccess = disable ? "Disabled" : "Enabled",
                changed = false });

        // SecurityStamp rotation invalidates existing DriverBearer tokens on their
        // next authenticated API request. A fresh login is required after re-enabling.
        // Use one Identity update and one SQL transaction for lockout + stamp.
        user.LockoutEnabled = true;
        user.LockoutEnd = disable ? DriverMobileAccess.SuspensionEndUtc : null;
        user.AccessFailedCount = 0;

        var result = await users.UpdateSecurityStampAsync(user);
        if (!result.Succeeded)
            return Conflict(new { message = "Mobile access could not be changed. Retry." });

        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Driver mobile access {Action}; DriverId={DriverId}; AdminId={AdminId}; AtUtc={AtUtc}; Reason={Reason}",
            disable ? "disabled" : "enabled", id,
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown",
            clock.GetUtcNow(), reason);

        return Ok(new { driverId = id, mobileAccess = disable ? "Disabled" : "Enabled",
            changed = true });
    }
}

public sealed class DriverAccessChangeRequest
{
    [Required, StringLength(240, MinimumLength = 8)]
    public string Reason { get; init; } = "";
}
