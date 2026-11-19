using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dalan.Application.Notifications;
using Dalan.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Dalan.Infrastructure.Notifications;

public sealed class OperationsNotificationRepository(DalanDbContext db) : IOperationsNotificationRepository
{
    public Task<OperationsNotice?> ReadSnapshotAsync(DateOnly day, string kind, CancellationToken cancellationToken) =>
        db.OperationsSnapshots.AsNoTracking().Where(x => x.DayUtc == day && x.Kind == kind)
            .Select(x => new OperationsNotice(x.DayUtc, x.Kind, x.Data)).SingleOrDefaultAsync(cancellationToken);

    public async Task<EmailEnvelope?> ClaimAsync(OperationsNotice notice, string from, IReadOnlyList<string> to,
        string subject, string text, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var identity = JsonSerializer.Serialize(new { notice.DayUtc, notice.Kind, from, to = to.Order(StringComparer.OrdinalIgnoreCase), subject, text });
        var id = "ops/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return await ClaimRow(id, now, () => new NotificationDelivery { Id = id, DayUtc = notice.DayUtc, Kind = notice.Kind, CreatedAtUtc = now,
            From = from, ToJson = JsonSerializer.Serialize(to), Subject = subject, Text = text }, cancellationToken);
    }

    public Task<EmailEnvelope?> ClaimExistingAsync(string id, DateTimeOffset now, CancellationToken cancellationToken) =>
        ClaimRow(id, now, null, cancellationToken);

    private async Task<EmailEnvelope?> ClaimRow(string id, DateTimeOffset now, Func<NotificationDelivery>? create, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var row = await db.NotificationDeliveries.FromSqlInterpolated($"SELECT * FROM NotificationDeliveries WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id}")
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            if (create is null) return null;
            row = create();
            db.NotificationDeliveries.Add(row);
        }
        if (row.NeedsReview || row.AcceptedAtUtc is not null || row.LeaseUntilUtc > now || row.NextAttemptAtUtc > now)
        { await transaction.CommitAsync(cancellationToken); return null; }
        // Resend's deduplication lasts 24 hours. Stop automatic ambiguous retries before that window expires.
        if (now - row.CreatedAtUtc >= TimeSpan.FromHours(20))
        { row.NeedsReview = true; await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return null; }
        row.Claim = Guid.NewGuid(); row.LeaseUntilUtc = now.AddMinutes(2); row.Attempts++;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(row.Id, row.Claim, row.From, JsonSerializer.Deserialize<string[]>(row.ToJson)!, row.Subject, row.Text);
    }

    public async Task FinishAsync(string id, Guid claim, string? providerId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await db.NotificationDeliveries.Where(x => x.Id == id && x.Claim == claim && x.AcceptedAtUtc == null)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.LeaseUntilUtc, (DateTimeOffset?)null)
                .SetProperty(x => x.AcceptedAtUtc, providerId == null ? (DateTimeOffset?)null : now)
                .SetProperty(x => x.ProviderId, providerId)
                .SetProperty(x => x.NextAttemptAtUtc, providerId == null ? now.AddMinutes(1) : (DateTimeOffset?)null), cancellationToken);
        db.ChangeTracker.Clear();
    }

    public async Task<IReadOnlyList<string>> PendingAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.NotificationDeliveries.AsNoTracking().Where(x => x.AcceptedAtUtc == null && !x.NeedsReview
            && (x.LeaseUntilUtc == null || x.LeaseUntilUtc <= now) && (x.NextAttemptAtUtc == null || x.NextAttemptAtUtc <= now))
            .OrderBy(x => x.CreatedAtUtc).Take(50).Select(x => x.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<EmailDeliveryStatus>> RecentAsync(CancellationToken cancellationToken) =>
        await db.NotificationDeliveries.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Take(50)
            .Select(x => new EmailDeliveryStatus(x.Id, x.CreatedAtUtc, x.Subject,
                x.AcceptedAtUtc != null ? "Accepted by Resend" : x.NeedsReview ? "Needs review" : "Pending / retrying", x.Attempts, x.AcceptedAtUtc))
            .ToListAsync(cancellationToken);
}
