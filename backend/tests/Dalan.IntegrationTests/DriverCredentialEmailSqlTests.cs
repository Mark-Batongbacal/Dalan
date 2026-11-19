using System.Net;
using System.Text.Json;
using Dalan.Application.Notifications;
using Dalan.Infrastructure.Notifications;
using Dalan.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dalan.IntegrationTests;

[Collection("SQL Server")]
public sealed class DriverCredentialEmailSqlTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Failed_credential_email_is_encrypted_and_retry_uses_the_same_provider_key()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var id = "driver-invite-" + suffix;
        var secret = "sensitive-one-time-token-" + suffix;
        var options = new DriverCredentialEmailOptions(true, "test");
        var clock = new AdjustableClock(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        var transport = new RecordingHandler { Fail = true };
        var protection = NewProtection();

        using (var first = fixture.CreateContext())
        {
            var outbox = NewOutbox(first, transport, options, clock, protection);
            Assert.Equal("queued", await outbox.QueueAsync(Message(id, secret), default));
        }
        await using (var inspect = fixture.CreateContext())
        {
            var row = await inspect.NotificationDeliveries.SingleAsync(x => x.Id == id);
            Assert.Equal("driver-invite", row.Kind);
            Assert.Equal("[]", row.ToJson);
            Assert.DoesNotContain(secret, row.Text);
            Assert.DoesNotContain("driver@dalan.test", row.Text);
            Assert.False(row.NeedsReview);
            Assert.Null(row.AcceptedAtUtc);
            Assert.Equal(1, row.Attempts);
        }

        // A fresh worker instance can decrypt the exact same queued ciphertext.
        transport.Fail = false;
        clock.Advance(TimeSpan.FromMinutes(2));
        using (var retry = fixture.CreateContext())
            await NewOutbox(retry, transport, options, clock, protection).RetryPendingAsync(default);

        Assert.Equal([id, id], transport.Ids);
        await using var verify = fixture.CreateContext();
        var delivered = await verify.NotificationDeliveries.SingleAsync(x => x.Id == id);
        Assert.NotNull(delivered.AcceptedAtUtc);
        Assert.Equal("provider-accepted", delivered.ProviderId);
        Assert.Equal(2, delivered.Attempts);
        Assert.Equal(string.Empty, delivered.Text); // Wiped after success.
    }

    [Fact]
    public async Task Credentials_are_never_consumed_by_operations_worker()
    {
        var id = "driver-recovery-" + Guid.NewGuid().ToString("N");
        using var db = fixture.CreateContext();
        var handler = new RecordingHandler { Fail = true };
        var clock = new AdjustableClock(DateTimeOffset.UtcNow);
        await NewOutbox(db, handler, new(true, "test"), clock, NewProtection())
            .QueueAsync(Message(id, "secret-reset-token"), default);

        var operations = new OperationsNotificationRepository(db);
        Assert.DoesNotContain(id, await operations.PendingAsync(clock.GetUtcNow(), default));
        Assert.Null(await operations.ClaimExistingAsync(id, clock.GetUtcNow(), default));
        Assert.DoesNotContain(
            await operations.RecentAsync(default), item => item.Id == id);
    }

    [Fact]
    public async Task Expired_pending_credentials_need_review_and_are_not_sent()
    {
        var id = "driver-recovery-" + Guid.NewGuid().ToString("N");
        var clock = new AdjustableClock(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero));
        var handler = new RecordingHandler { Fail = true };
        var protection = NewProtection();
        using (var first = fixture.CreateContext())
            await NewOutbox(first, handler, new(true, "test"), clock, protection)
                .QueueAsync(Message(id, "reset-token-must-expire"), default);

        clock.Advance(TimeSpan.FromHours(21));
        handler.Fail = false;
        using (var retry = fixture.CreateContext())
            await NewOutbox(retry, handler, new(true, "test"), clock, protection)
                .RetryPendingAsync(default);

        Assert.Single(handler.Ids); // Only the initial failed send.
        await using var verify = fixture.CreateContext();
        var row = await verify.NotificationDeliveries.SingleAsync(x => x.Id == id);
        Assert.True(row.NeedsReview);
        Assert.Empty(row.Text);
        Assert.Null(row.AcceptedAtUtc);
    }

    private static EmailEnvelope Message(string id, string token) =>
        new(id, Guid.Empty, "no-reply@dalan.test", ["driver@dalan.test"],
            "Dalan | Reset password", "https://dalan.test/#token=" + token);

    private static IDataProtectionProvider NewProtection()
    {
        var services = new ServiceCollection();
        services.AddDataProtection().SetApplicationName("Dalan.TestCredentialEmail");
        return services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    }

    private static DriverCredentialEmailOutbox NewOutbox(
        DalanDbContext db, RecordingHandler handler, DriverCredentialEmailOptions options,
        TimeProvider clock, IDataProtectionProvider protection)
    {
        var transport = new ResendEmailSender(new HttpClient(handler)
            { BaseAddress = new("https://api.resend.com/") },
            new ResendOptions { Enabled = true, ApiKey = "fake" });
        return new(db, transport, protection, options, clock, NullLogger<DriverCredentialEmailOutbox>.Instance);
    }

    private sealed class AdjustableClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan duration) => current = current.Add(duration);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public bool Fail;
        public List<string> Ids { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Ids.Add(request.Headers.GetValues("Idempotency-Key").Single());
            return Task.FromResult(new HttpResponseMessage(Fail
                ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            {
                Content = new StringContent(Fail ? "unavailable" : "{\"id\":\"provider-accepted\"}")
            });
        }
    }
}
