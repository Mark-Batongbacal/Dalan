using System.Data;
using Dalan.Application.Identity;
using Dalan.Contracts.Identity;
using Dalan.Infrastructure.Persistence;
using Dalan.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
namespace Dalan.Infrastructure.Repositories;
public sealed class DriverAccessRepository(
    DalanDbContext db,
    UserManager<IdentityUser> users,
    TimeProvider clock,
    ILogger<DriverAccessRepository> logger) : LifecycleResponses, IDriverAccessRepository
{

    public async Task<LifecycleResult> Status(Guid id, CancellationToken cancellationToken)
    {
        if (!await db.Drivers.AnyAsync(x => x.Id == id, cancellationToken))
            return NotFound();

        var account = await db.DriverAccounts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.DriverId == id, cancellationToken);
        if (account is null)
            return Ok(new DriverMobileAccountStatus(id, "NoAccount"));

        var user = await users.FindByIdAsync(account.UserId);
        if (user is null || !await users.IsInRoleAsync(user, "Driver"))
            return Conflict(new { message = "The linked Driver Identity account is invalid." });

        var status = DriverMobileAccess.IsSuspended(user)
            ? "Disabled"
            : await users.HasPasswordAsync(user) ? "Enabled" : "PendingActivation";
        return Ok(new DriverMobileAccountStatus(id, status, user.Email));
    }

    public Task<LifecycleResult> Disable(
        Guid id, DriverAccessChangeRequest request, string actorUserId, CancellationToken cancellationToken) =>
        Change(id, request, disable: true, actorUserId, cancellationToken);

    public Task<LifecycleResult> Enable(
        Guid id, DriverAccessChangeRequest request, string actorUserId, CancellationToken cancellationToken) =>
        Change(id, request, disable: false, actorUserId, cancellationToken);

    private async Task<LifecycleResult> Change(
        Guid id, DriverAccessChangeRequest request, bool disable, string actorUserId, CancellationToken cancellationToken)
    {
        // Both the reason and the authenticated Admin are included in a structured
        // server log. DLN-35 separately adds persistent read-only audit history.
        var reason = string.Join(" ", request.Reason
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (reason.Length is < 8 or > 240)
            return BadRequest(new { message = "Provide a reason between 8 and 240 characters." });

        await using var transaction =
            await PersistenceTransaction.BeginAsync(db, cancellationToken);
        var account = await db.DriverAccounts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.DriverId == id, cancellationToken);
        if (account is null) return NotFound();

        var user = await users.FindByIdAsync(account.UserId);
        if (user is null || !await users.IsInRoleAsync(user, "Driver"))
            return Conflict(new { message = "The linked Driver Identity account is invalid." });

        if (DriverMobileAccess.IsSuspended(user) == disable)
            return Ok(new
            {
                driverId = id,
                mobileAccess = disable ? "Disabled" : "Enabled",
                changed = false
            });

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
            actorUserId,
            clock.GetUtcNow(), reason);

        return Ok(new
        {
            driverId = id,
            mobileAccess = disable ? "Disabled" : "Enabled",
            changed = true
        });
    }
}
