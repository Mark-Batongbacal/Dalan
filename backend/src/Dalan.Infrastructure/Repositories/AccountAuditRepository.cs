using Dalan.Application.Identity;
using Dalan.Contracts.Identity;
using Dalan.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace Dalan.Infrastructure.Repositories;
public sealed class AccountAuditRepository(DalanDbContext db, IConfiguration configuration) : LifecycleResponses, IAccountAuditRepository
{

    public async Task<LifecycleResult> List(Guid? driverId, DateTimeOffset? beforeUtc,
        int take = 50, CancellationToken cancellationToken = default, Guid? beforeId = null)
    {
        if (!bool.TryParse(configuration["AccountAudit:Enabled"], out var enabled) || !enabled)
            return StatusCode(503,
                new { message = "Account audit history has not been enabled." });
        if (take is < 1 or > 100) return BadRequest(new { message = "take must be 1–100." });

        var query = db.AccountAccessAudits.AsNoTracking();
        if (driverId is Guid id)
            query = query.Where(x => x.DriverId == id);
        if (beforeUtc is DateTimeOffset before)
            query = beforeId is Guid cursor
                ? query.Where(x => x.OccurredAtUtc < before || (x.OccurredAtUtc == before && x.Id.CompareTo(cursor) < 0))
                : query.Where(x => x.OccurredAtUtc < before);

        var items = await query
            .OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id)
            .Take(take)
            .Select(x => new AccountAuditItem(x.Id, x.OccurredAtUtc, x.Action, x.Outcome,
                x.ActorUserId, x.TargetUserId, x.DriverId, x.Reason, x.CorrelationId))
            .ToListAsync(cancellationToken);
        return Ok(items);
    }
}
