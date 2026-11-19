using System.Net;
using System.Text.Json;
using Dalan.Application.Notifications;
using Dalan.Infrastructure.Notifications;
using Dalan.Infrastructure.Routing;
using Xunit;

namespace Dalan.UnitTests;

public sealed class ExternalProviderTests
{
    [Fact]
    public async Task Google_requests_text_fields_only_and_keeps_provider_credits()
    {
        using var handler = new Handler("""{"places":[{"displayName":{"text":"Warehouse"},"formattedAddress":"Manila","googleMapsUri":"https://maps.google.com/?cid=123","attributions":[{"provider":"Source","providerUri":"https://source.test/"}]}]}""");
        var service = new GooglePlacesTextSearch(new HttpClient(handler) { BaseAddress = new("https://places.googleapis.com/") }, "test-key");
        var item = Assert.Single(await service.SearchAsync("  Manila  "));
        Assert.Equal("Warehouse", item.Name);
        Assert.Equal("Manila", item.Address);
        Assert.Equal("Source", Assert.Single(item.Attributions).Provider);
        Assert.Equal("/v1/places:searchText", handler.Uri!.AbsolutePath);
        Assert.Equal("test-key", handler.Headers["X-Goog-Api-Key"]);
        Assert.DoesNotContain("location", handler.Headers["X-Goog-FieldMask"], StringComparison.OrdinalIgnoreCase);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("Manila", body.RootElement.GetProperty("textQuery").GetString());
        Assert.Equal(10, body.RootElement.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task Google_discards_unsafe_links_and_does_not_call_for_invalid_input()
    {
        using var handler = new Handler("""{"places":[{"displayName":{"text":"A"},"googleMapsUri":"https://google.com.evil.test/","attributions":[{"provider":"Bad link","providerUri":"javascript:alert(1)"}]}]}""");
        var service = new GooglePlacesTextSearch(new HttpClient(handler) { BaseAddress = new("https://places.googleapis.com/") }, "key");
        await Assert.ThrowsAsync<ArgumentException>(() => service.SearchAsync("ab"));
        Assert.Null(handler.Uri);
        var item = Assert.Single(await service.SearchAsync("Manila"));
        Assert.Null(item.GoogleMapsUri); Assert.Null(Assert.Single(item.Attributions).Uri);
    }

    [Fact]
    public async Task Google_provider_failure_is_not_an_empty_success()
    {
        using var handler = new Handler("{}", HttpStatusCode.TooManyRequests);
        var service = new GooglePlacesTextSearch(new HttpClient(handler) { BaseAddress = new("https://places.googleapis.com/") }, "key");
        await Assert.ThrowsAsync<HttpRequestException>(() => service.SearchAsync("Manila"));
    }

    [Fact]
    public async Task Resend_sends_saved_envelope_with_stable_idempotency_key()
    {
        using var handler = new Handler("""{"id":"email-123"}""");
        var sender = new ResendEmailSender(new HttpClient(handler) { BaseAddress = new("https://api.resend.com/") },
            new() { Enabled = true, ApiKey = "test-key" });
        var envelope = new EmailEnvelope("ops/stable-id", Guid.NewGuid(), "sender@example.test", ["recipient@example.test"], "Overdue", "Records: 2");
        Assert.Equal("email-123", await sender.SendAsync(envelope, default));
        Assert.Equal("/emails", handler.Uri!.AbsolutePath);
        Assert.Equal("Bearer test-key", handler.Headers["Authorization"]);
        Assert.Equal(envelope.Id, handler.Headers["Idempotency-Key"]);
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.Equal(envelope.From, json.RootElement.GetProperty("from").GetString());
        Assert.Equal(envelope.To[0], json.RootElement.GetProperty("to")[0].GetString());
        Assert.Equal(envelope.Text, json.RootElement.GetProperty("text").GetString());
    }

    [Theory]
    [InlineData("{}", 200)]
    [InlineData("{\"id\":\"\"}", 200)]
    [InlineData("{}", 429)]
    public async Task Resend_does_not_acknowledge_unaccepted_mail(string json, int status)
    {
        using var handler = new Handler(json, (HttpStatusCode)status);
        var sender = new ResendEmailSender(new HttpClient(handler) { BaseAddress = new("https://api.resend.com/") }, new() { Enabled = true, ApiKey = "key" });
        await Assert.ThrowsAnyAsync<Exception>(() => sender.SendAsync(new("ops/test", Guid.NewGuid(), "a@test.test", ["b@test.test"], "test", "test"), default));
    }

    private sealed class Handler(string json, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public Uri? Uri;
        public string? Body;
        public Dictionary<string, string> Headers = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri; Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value));
            return new(status) { Content = new StringContent(json) };
        }
    }
}
