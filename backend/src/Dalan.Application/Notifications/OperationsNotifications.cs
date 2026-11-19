using System.Text.Json;

namespace Dalan.Application.Notifications;

public sealed class ResendOptions
{
    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = "";
    public string From { get; set; } = "";
    public string Recipients { get; set; } = "";
}
public sealed record OperationsNotice(DateOnly DayUtc, string Kind, string Data);
public sealed record EmailEnvelope(string Id, Guid Claim, string From, IReadOnlyList<string> To, string Subject, string Text);
public sealed record EmailDeliveryStatus(string Id, DateTimeOffset CreatedAtUtc, string Subject, string Status, int Attempts, DateTimeOffset? AcceptedAtUtc);
public interface IEmailSender
{
    Task<string> SendAsync(EmailEnvelope envelope, CancellationToken cancellationToken);
}
public interface IOperationsNotificationRepository
{
    Task<OperationsNotice?> ReadSnapshotAsync(DateOnly day, string kind, CancellationToken cancellationToken);
    Task<EmailEnvelope?> ClaimAsync(OperationsNotice notice, string from, IReadOnlyList<string> to,
        string subject, string text, DateTimeOffset now, CancellationToken cancellationToken);
    Task FinishAsync(string id, Guid claim, string? providerId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> PendingAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<EmailEnvelope?> ClaimExistingAsync(string id, DateTimeOffset now, CancellationToken cancellationToken);
    Task<IReadOnlyList<EmailDeliveryStatus>> RecentAsync(CancellationToken cancellationToken);
}
public interface IOperationsNotificationService
{
    Task PublishAsync(DateOnly day, string kind, CancellationToken cancellationToken);
    Task RetryPendingAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<EmailDeliveryStatus>> RecentAsync(CancellationToken cancellationToken);
}

public sealed class OperationsNotificationService(IOperationsNotificationRepository repository,
    IEmailSender sender, ResendOptions options, TimeProvider clock) : IOperationsNotificationService
{
    public async Task PublishAsync(DateOnly day, string kind, CancellationToken cancellationToken)
    {
        if (!options.Enabled) return;
        var snapshot = await repository.ReadSnapshotAsync(day, kind, cancellationToken);
        if (snapshot is null) return;
        using var data = JsonDocument.Parse(snapshot.Data);
        var count = data.RootElement.GetArrayLength();
        if (count == 0 && kind != "dispatch") return;
        var content = OperationsEmailContent.Create(snapshot);
        var recipients = options.Recipients.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var envelope = await repository.ClaimAsync(snapshot, options.From, recipients, content.Subject, content.Text, clock.GetUtcNow(), cancellationToken);
        if (envelope is null) return;
        await SendAsync(envelope, cancellationToken);
    }

    private async Task SendAsync(EmailEnvelope envelope, CancellationToken cancellationToken)
    {
        try
        {
            var id = await sender.SendAsync(envelope, cancellationToken);
            await repository.FinishAsync(envelope.Id, envelope.Claim, id, clock.GetUtcNow(), cancellationToken);
        }
        catch
        {
            // Clear the lease for a retry; preserve payload/idempotency identity after ambiguous provider failures.
            await repository.FinishAsync(envelope.Id, envelope.Claim, null, clock.GetUtcNow(), CancellationToken.None);
            throw;
        }
    }

    public async Task RetryPendingAsync(CancellationToken cancellationToken)
    {
        if (!options.Enabled) return;
        foreach (var id in await repository.PendingAsync(clock.GetUtcNow(), cancellationToken))
            if (await repository.ClaimExistingAsync(id, clock.GetUtcNow(), cancellationToken) is EmailEnvelope envelope)
                await SendAsync(envelope, cancellationToken);
    }

    public Task<IReadOnlyList<EmailDeliveryStatus>> RecentAsync(CancellationToken cancellationToken) => repository.RecentAsync(cancellationToken);
}
