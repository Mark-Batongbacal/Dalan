using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dalan.Application.Notifications;

namespace Dalan.Infrastructure.Notifications;

public sealed class ResendEmailSender(HttpClient client, ResendOptions options) : IEmailSender
{
    public async Task<string> SendAsync(EmailEnvelope envelope, CancellationToken cancellationToken)
    {
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.ApiKey)) throw new InvalidOperationException("Resend is not configured.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "emails")
        {
            Content = JsonContent.Create(new { from = envelope.From, to = envelope.To, subject = envelope.Subject, text = envelope.Text })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        request.Headers.Add("Idempotency-Key", envelope.Id);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (!body.RootElement.TryGetProperty("id", out var id) || string.IsNullOrWhiteSpace(id.GetString()))
            throw new InvalidDataException("Resend did not acknowledge the email.");
        return id.GetString()!;
    }
}
