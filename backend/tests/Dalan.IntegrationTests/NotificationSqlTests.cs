using Dalan.Application.Notifications;
using Dalan.Infrastructure.Notifications;
using Dalan.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Dalan.IntegrationTests;

[Collection("SQL Server")]
public sealed class NotificationSqlTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Failed_delivery_retries_saved_envelope_even_if_configuration_changes()
    {
        var clock = new Clock { Now = new(2026, 11, 5, 10, 0, 0, TimeSpan.Zero) };
        var day = DateOnly.FromDateTime(clock.Now.UtcDateTime);
        await using var db = fixture.CreateContext();
        db.OperationsSnapshots.AddRange(new OperationsSnapshot { DayUtc = day, Kind = "overdue-10", Data = "[{\"Id\":1}]" },
            new OperationsSnapshot { DayUtc = day, Kind = "maintenance", Data = "[]" });
        await db.SaveChangesAsync();
        var repository = new OperationsNotificationRepository(db);
        var sender = new Sender();
        var options = new ResendOptions { Enabled = false, From = "from@test.test", Recipients = "to@test.test" };
        var service = new OperationsNotificationService(repository, sender, options, clock);
        await service.PublishAsync(day, "overdue-10", default);
        Assert.Empty(sender.Sent);
        options.Enabled = true;
        await service.PublishAsync(day, "maintenance", default);
        Assert.Empty(sender.Sent); // Empty reminder snapshots do not spam recipients.
        await Assert.ThrowsAsync<HttpRequestException>(() => service.PublishAsync(day, "overdue-10", default));
        var original = Assert.Single(sender.Sent);
        Assert.Contains("Overdue deliveries: 1", original.Text);
        options.From = "changed@test.test"; options.Recipients = "changed-recipient@test.test";
        clock.Now = clock.Now.AddMinutes(1); sender.Fail = false;
        await service.RetryPendingAsync(default);
        Assert.Equal(2, sender.Sent.Count);
        var retried = sender.Sent[1];
        Assert.Equal(original.Id, retried.Id); Assert.Equal(original.From, retried.From);
        Assert.Equal(original.To, retried.To); Assert.Equal(original.Text, retried.Text);
        await service.RetryPendingAsync(default);
        Assert.Equal(2, sender.Sent.Count);
        var status = Assert.Single(await repository.RecentAsync(default), x => x.Id == original.Id);
        Assert.Equal("Accepted by Resend", status.Status);
    }

    private sealed class Clock : TimeProvider
    { public DateTimeOffset Now; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Sender : IEmailSender
    {
        public bool Fail = true;
        public List<EmailEnvelope> Sent = [];
        public Task<string> SendAsync(EmailEnvelope envelope, CancellationToken cancellationToken)
        { Sent.Add(envelope); if (Fail) throw new HttpRequestException("Simulated ambiguous provider failure"); return Task.FromResult("test-provider-id"); }
    }

    [Fact]
    public async Task Concurrent_claims_retry_original_payload_and_stop_before_provider_deduplication_expires()
    {
        var now = DateTimeOffset.UtcNow;
        var notice = new OperationsNotice(DateOnly.FromDateTime(now.UtcDateTime), "dispatch", "[]");
        var subject = "Test-" + Guid.NewGuid();
        async Task<EmailEnvelope?> Claim()
        { await using var db = fixture.CreateContext(); return await new OperationsNotificationRepository(db).ClaimAsync(notice, "from@test.test", ["to@test.test"], subject, "original text", now, default); }
        var claims = await Task.WhenAll(Claim(), Claim());
        var envelope = Assert.Single(claims, x => x is not null)!;
        await using var db = fixture.CreateContext();
        var repository = new OperationsNotificationRepository(db);
        await repository.FinishAsync(envelope.Id, Guid.NewGuid(), "wrong-lease", now, default);
        Assert.Null((await db.NotificationDeliveries.AsNoTracking().SingleAsync(x => x.Id == envelope.Id)).AcceptedAtUtc);
        await repository.FinishAsync(envelope.Id, envelope.Claim, null, now, default);
        Assert.Null(await repository.ClaimExistingAsync(envelope.Id, now.AddSeconds(30), default));
        var retry = await repository.ClaimExistingAsync(envelope.Id, now.AddMinutes(1), default);
        Assert.NotNull(retry); Assert.Equal(envelope.Id, retry.Id); Assert.Equal(envelope.Text, retry.Text);
        Assert.Equal(envelope.To, retry.To); Assert.NotEqual(envelope.Claim, retry.Claim);
        await repository.FinishAsync(envelope.Id, envelope.Claim, "stale-ack", now.AddMinutes(1), default);
        Assert.Null((await db.NotificationDeliveries.AsNoTracking().SingleAsync(x => x.Id == envelope.Id)).AcceptedAtUtc);
        await repository.FinishAsync(retry.Id, retry.Claim, "resend-id", now.AddMinutes(1), default);
        Assert.Null(await repository.ClaimExistingAsync(envelope.Id, now.AddMinutes(3), default));
        var saved = await db.NotificationDeliveries.AsNoTracking().SingleAsync(x => x.Id == envelope.Id);
        Assert.Equal("resend-id", saved.ProviderId); Assert.Equal(2, saved.Attempts);

        var ambiguous = (await repository.ClaimAsync(notice, "from@test.test", ["to@test.test"], subject + "-ambiguous", "ambiguous", now, default))!;
        Assert.Null(await repository.ClaimExistingAsync(ambiguous.Id, now.AddSeconds(30), default));
        Assert.Null(await repository.ClaimExistingAsync(ambiguous.Id, now.AddHours(20), default));
        Assert.True((await db.NotificationDeliveries.AsNoTracking().SingleAsync(x => x.Id == ambiguous.Id)).NeedsReview);
        Assert.DoesNotContain(ambiguous.Id, await repository.PendingAsync(now.AddHours(21), default));
    }
}
