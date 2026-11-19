using System.Net;
using Dalan.Application.Notifications;
using Dalan.Infrastructure.Notifications;
using Xunit;

namespace Dalan.UnitTests;

public sealed class DriverCredentialEmailRoutingTests
{
    [Theory]
    [InlineData("driver-invite-123")]
    [InlineData("driver-recovery-456")]
    public async Task Driver_credential_envelopes_are_routed_into_the_outbox(string id)
    {
        var handler = new RecordingHandler();
        var queue = new RecordingOutbox();
        var sender = NewSender(handler, queue);
        var result = await sender.SendAsync(new EmailEnvelope(id, Guid.Empty, "sender@dalan.test",
            ["driver@dalan.test"], "Set password", "https://dalan.test/#token=secret"), default);
        Assert.Equal("queued", result);
        Assert.Equal(id, Assert.Single(queue.Queued));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Operations_email_still_uses_the_existing_Resend_transport()
    {
        var handler = new RecordingHandler();
        var queue = new RecordingOutbox();
        var sender = NewSender(handler, queue);
        var result = await sender.SendAsync(new EmailEnvelope("ops/123", Guid.Empty, "sender@dalan.test",
            ["admin@dalan.test"], "Daily summary", "No private tokens"), default);
        Assert.Equal("resend-acknowledged", result);
        Assert.Equal(1, handler.Calls);
        Assert.Empty(queue.Queued);
    }

    private static OutboxRoutedEmailSender NewSender(RecordingHandler handler, RecordingOutbox queue)
    {
        var client = new HttpClient(handler) { BaseAddress = new("https://api.resend.com/") };
        var options = new ResendOptions { Enabled = true, ApiKey = "test-key" };
        return new(new ResendEmailSender(client, options), queue);
    }

    private sealed class RecordingOutbox : IDriverCredentialEmailOutbox
    {
        public List<string> Queued { get; } = [];
        public Task<string> QueueAsync(EmailEnvelope envelope, CancellationToken cancellationToken)
        { Queued.Add(envelope.Id); return Task.FromResult("queued"); }
        public Task RetryPendingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal("ops/123", request.Headers.GetValues("Idempotency-Key").Single());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":\"resend-acknowledged\"}")
            });
        }
    }
}
