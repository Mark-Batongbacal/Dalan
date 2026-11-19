using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Dalan.Application.Notifications;
using Dalan.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dalan.Infrastructure.Notifications;

public sealed record DriverCredentialEmailOptions(bool Enabled, string KeyRingPath);

/// <summary>
/// All temporary credentials remain encrypted inside NotificationDeliveries.Text.
/// This service is shared by Dalan.Web and Dalan.Worker; both must have the SAME
/// persistent Data Protection key ring and application discriminator.
/// </summary>
public interface IDriverCredentialEmailOutbox
{
    Task<string> QueueAsync(EmailEnvelope message, CancellationToken cancellationToken);
    Task RetryPendingAsync(CancellationToken cancellationToken);
}

public sealed class DriverCredentialEmailOutbox(
    DalanDbContext db, ResendEmailSender transport, IDataProtectionProvider protection,
    DriverCredentialEmailOptions options, TimeProvider clock,
    ILogger<DriverCredentialEmailOutbox> logger) : IDriverCredentialEmailOutbox
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan AutomaticRetryLimit = TimeSpan.FromHours(20);
    private readonly IDataProtector protector = protection.CreateProtector("Dalan.DriverCredentialEmail.v1");

    private sealed record ProtectedMessage(string From, string[] To, string Subject, string Text);

    public async Task<string> QueueAsync(EmailEnvelope message, CancellationToken cancellationToken)
    {
        if (!options.Enabled)
            throw new InvalidOperationException("Driver credential email outbox has not been enabled.");
        var kind = Kind(message.Id);
        var now = clock.GetUtcNow();
        // The provider's 24-hour deduplication key is exactly the persisted row ID.
        // No plaintext token, target address, or reset URL is kept in SQL.
        var protectedPayload = protector.Protect(JsonSerializer.Serialize(
            new ProtectedMessage(message.From, message.To.ToArray(), message.Subject, message.Text)));

        db.NotificationDeliveries.Add(new NotificationDelivery
        {
            Id = message.Id, Kind = kind, DayUtc = DateOnly.FromDateTime(now.UtcDateTime),
            CreatedAtUtc = now, From = message.From, ToJson = "[]",
            Subject = "Dalan Driver account email", Text = protectedPayload
        });
        await db.SaveChangesAsync(cancellationToken);
        await TrySendAsync(message.Id, cancellationToken);
        // Queued is not equivalent to provider-accepted or inbox-delivered.
        return "queued";
    }

    public async Task RetryPendingAsync(CancellationToken cancellationToken)
    {
        if (!options.Enabled) return;
        var now = clock.GetUtcNow();
        // Keep these separate from operations snapshot email rows. Never send
        // protected payloads using the operations notification worker.
        var pending = await db.NotificationDeliveries.AsNoTracking()
            .Where(x => (x.Kind == "driver-invite" || x.Kind == "driver-recovery")
                && x.AcceptedAtUtc == null && !x.NeedsReview
                && (x.LeaseUntilUtc == null || x.LeaseUntilUtc <= now)
                && (x.NextAttemptAtUtc == null || x.NextAttemptAtUtc <= now))
            .OrderBy(x => x.CreatedAtUtc).Take(50).Select(x => x.Id)
            .ToListAsync(cancellationToken);
        foreach (var id in pending)
            await TrySendAsync(id, cancellationToken);
    }

    private async Task TrySendAsync(string id, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var now = clock.GetUtcNow();
        NotificationDelivery? row;
        await using (var transaction =
            await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken))
        {
            row = await db.NotificationDeliveries.FromSqlInterpolated(
                $"SELECT * FROM NotificationDeliveries WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id}")
                .SingleOrDefaultAsync(cancellationToken);
            if (row is null || (row.Kind != "driver-invite" && row.Kind != "driver-recovery")
                || row.AcceptedAtUtc is not null || row.NeedsReview
                || row.LeaseUntilUtc > now || row.NextAttemptAtUtc > now)
                return;

            // Stop before Resend's 24-hour idempotency window or Identity's
            // default token expiry; a stale reset link must not be mailed.
            if (now - row.CreatedAtUtc >= AutomaticRetryLimit)
            {
                row.NeedsReview = true;
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            row.Claim = Guid.NewGuid();
            row.LeaseUntilUtc = now.Add(Lease);
            row.Attempts++;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        // Copy the claim: no EF tracked row remains after the SQL update.
        var claim = row.Claim;
        string? providerId = null;
        bool corrupted = false;
        try
        {
            var message = JsonSerializer.Deserialize<ProtectedMessage>(protector.Unprotect(row.Text))
                ?? throw new CryptographicException("Invalid protected credential email.");
            var envelope = new EmailEnvelope(id, claim, message.From, message.To, message.Subject, message.Text);
            providerId = await transport.SendAsync(envelope, cancellationToken);
        }
        catch (CryptographicException)
        {
            corrupted = true;
            logger.LogError("Credential email could not be decrypted; delivery requires manual review. Id={MessageId}", id);
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            // Never log the secret-bearing envelope or provider response body.
            logger.LogWarning("Credential email attempt deferred. Id={MessageId}; Error={ErrorType}",
                id, error.GetType().Name);
        }
        finally
        {
            await db.NotificationDeliveries
                .Where(x => x.Id == id && x.Claim == claim && x.AcceptedAtUtc == null)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(x => x.LeaseUntilUtc, (DateTimeOffset?)null)
                    .SetProperty(x => x.AcceptedAtUtc, providerId is null ? (DateTimeOffset?)null : clock.GetUtcNow())
                    .SetProperty(x => x.ProviderId, providerId)
                    .SetProperty(x => x.NextAttemptAtUtc,
                        providerId is null && !corrupted
                            ? (DateTimeOffset?)clock.GetUtcNow().Add(RetryDelay) : null)
                    .SetProperty(x => x.NeedsReview, corrupted), CancellationToken.None);
            db.ChangeTracker.Clear();
        }
    }

    private static string Kind(string id) =>
        id.StartsWith("driver-invite-", StringComparison.Ordinal) ? "driver-invite" :
        id.StartsWith("driver-recovery-", StringComparison.Ordinal) ? "driver-recovery" :
        throw new ArgumentException("Only Driver invitation/recovery envelopes may use the credential outbox.", nameof(id));
}

public sealed class OutboxRoutedEmailSender(
    ResendEmailSender transport, IDriverCredentialEmailOutbox credentials) : IEmailSender
{
    public Task<string> SendAsync(EmailEnvelope envelope, CancellationToken cancellationToken) =>
        envelope.Id.StartsWith("driver-invite-", StringComparison.Ordinal)
        || envelope.Id.StartsWith("driver-recovery-", StringComparison.Ordinal)
            ? credentials.QueueAsync(envelope, cancellationToken)
            : transport.SendAsync(envelope, cancellationToken);
}
