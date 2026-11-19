using Dalan.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dalan.Web.Controllers;

// Read-only Admin API; Codex owns any future audit-history display.
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/account-audit")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountAuditController(DalanDbContext db, IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid? driverId, DateTimeOffset? beforeUtc,
        int take = 50, CancellationToken cancellationToken = default)
    {
        if (!bool.TryParse(configuration["AccountAudit:Enabled"], out var enabled) || !enabled)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Account audit history has not been enabled." });
        if (take is < 1 or > 100) return BadRequest(new { message = "take must be 1–100." });

        var query = db.AccountAccessAudits.AsNoTracking();
        if (driverId is Guid id)
            query = query.Where(x => x.DriverId == id);
        if (beforeUtc is DateTimeOffset before)
            query = query.Where(x => x.OccurredAtUtc < before);

        var items = await query
            .OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id)
            .Take(take)
            .Select(x => new
            {
                x.Id, x.OccurredAtUtc, x.Action, x.Outcome,
                x.ActorUserId, x.TargetUserId, x.DriverId, x.Reason, x.CorrelationId
            })
            .ToListAsync(cancellationToken);
        return Ok(items);
    }
}
