using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Dalan.Application.Notifications;
using Dalan.Domain.Drivers;
using Dalan.Infrastructure.Persistence;
using Dalan.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Dalan.IntegrationTests;

[Collection("SQL Server")]
public sealed class DriverRecoverySqlTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Driver_can_reset_password_once_and_existing_mobile_token_is_revoked()
    {
        var seeded = await SeedDriver();
        var sender = new FakeSender();
        using var web = Factory(sender);
        using var client = web.CreateClient(new() { BaseAddress = new("https://localhost") });
        using var mobileFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Testing").UseSetting("ConnectionStrings:Dalan", fixture.ConnectionString));
        using var mobile = mobileFactory.CreateClient(new() { BaseAddress = new("https://localhost") });
        await Login(mobile, seeded.Email, seeded.Password);
        Assert.Equal(HttpStatusCode.OK, (await mobile.GetAsync("/api/driver/session")).StatusCode);

        using var requested = await client.PostAsJsonAsync("/api/driver-recovery/request", new { email = seeded.Email });
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        Assert.True(requested.Headers.CacheControl?.NoStore == true);
        var envelope = Assert.Single(sender.Messages);
        Assert.Equal(seeded.Email, Assert.Single(envelope.To));
        var resetToken = ExtractToken(envelope.Text);
        const string newPassword = "Driver-NewPassword#2026";

        using var wrong = await client.PostAsJsonAsync("/api/driver-recovery/reset",
            new { email = "wrong-" + seeded.Email, token = resetToken, password = newPassword });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        using var reset = await client.PostAsJsonAsync("/api/driver-recovery/reset",
            new { email = seeded.Email, token = resetToken, password = newPassword });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        using var reuse = await client.PostAsJsonAsync("/api/driver-recovery/reset",
            new { email = seeded.Email, token = resetToken, password = "Driver-AnotherPass#2026" });
        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await mobile.GetAsync("/api/driver/session")).StatusCode);
        using var oldLogin = await mobile.PostAsJsonAsync("/api/driver/login",
            new { email = seeded.Email, password = seeded.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);

        await Login(mobile, seeded.Email, newPassword);
        Assert.Equal(HttpStatusCode.OK, (await mobile.GetAsync("/api/driver/tasks")).StatusCode);
        await using var db = fixture.CreateContext();
        Assert.True(await db.DriverAccounts.AnyAsync(x => x.DriverId == seeded.DriverId));
    }

    [Fact]
    public async Task Request_replies_identically_for_unknown_staff_and_real_driver_addresses()
    {
        var seeded = await SeedDriver();
        var sender = new FakeSender();
        using var web = Factory(sender);
        using var client = web.CreateClient(new() { BaseAddress = new("https://localhost") });

        using var known = await client.PostAsJsonAsync("/api/driver-recovery/request", new { email = seeded.Email });
        using var unknown = await client.PostAsJsonAsync("/api/driver-recovery/request",
            new { email = "unknown-" + Guid.NewGuid().ToString("N") + "@dalan.test" });
        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        Assert.Single(sender.Messages);
    }

    [Fact]
    public async Task Password_recovery_is_disabled_by_default_and_not_rate_limit_free()
    {
        var sender = new FakeSender();
        using var disabled = Factory(sender, enabled: false);
        using var client = disabled.CreateClient(new() { BaseAddress = new("https://localhost") });
        using var rejected = await client.PostAsJsonAsync("/api/driver-recovery/request",
            new { email = "driver@dalan.test" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
        Assert.Empty(sender.Messages);

        using var enabled = Factory(sender);
        using var rateClient = enabled.CreateClient(new() { BaseAddress = new("https://localhost") });
        for (var i = 0; i < 10; i++)
        {
            using var response = await rateClient.PostAsJsonAsync("/api/driver-recovery/request",
                new { email = "notfound@dalan.test" });
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }
        using var throttled = await rateClient.PostAsJsonAsync("/api/driver-recovery/request",
            new { email = "notfound@dalan.test" });
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.Empty(sender.Messages);
    }

    [Fact]
    public async Task Failed_email_send_keeps_response_generic_without_changing_password()
    {
        var seeded = await SeedDriver();
        var sender = new FakeSender { Fail = true };
        using var web = Factory(sender);
        using var client = web.CreateClient(new() { BaseAddress = new("https://localhost") });
        using var response = await client.PostAsJsonAsync("/api/driver-recovery/request", new { email = seeded.Email });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(sender.Messages);

        using var api = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Testing").UseSetting("ConnectionStrings:Dalan", fixture.ConnectionString));
        using var mobile = api.CreateClient(new() { BaseAddress = new("https://localhost") });
        await Login(mobile, seeded.Email, seeded.Password);
        Assert.Equal(HttpStatusCode.OK, (await mobile.GetAsync("/api/driver/session")).StatusCode);
    }

    private async Task<(Guid DriverId, string Email, string Password)> SeedDriver()
    {
        var random = Guid.NewGuid().ToString("N");
        var driver = Driver.Register("Reset Driver", "09123456789", random,
            DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1));
        var email = random + "@dalan.test";
        var password = "Driver-Original#" + Convert.ToHexString(RandomNumberGenerator.GetBytes(9));
        using var web = Factory(new FakeSender());
        using var scope = web.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        if (!await roles.RoleExistsAsync("Driver"))
            Assert.True((await roles.CreateAsync(new IdentityRole("Driver"))).Succeeded);
        var user = new IdentityUser { Email = email, UserName = email, EmailConfirmed = true };
        Assert.True((await users.CreateAsync(user, password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, "Driver")).Succeeded);
        var db = scope.ServiceProvider.GetRequiredService<DalanDbContext>();
        db.AddRange(driver, new DriverAccount { DriverId = driver.Id, UserId = user.Id });
        await db.SaveChangesAsync();
        return (driver.Id, email, password);
    }

    private static string ExtractToken(string text)
    {
        var link = text.Split('\n').Single(x => x.StartsWith("https://", StringComparison.Ordinal));
        var fragment = new Uri(link).Fragment.TrimStart('#');
        var value = fragment.Split('&').Single(x => x.StartsWith("token=", StringComparison.Ordinal));
        return Uri.UnescapeDataString(value["token=".Length..]);
    }

    private WebApplicationFactory<WebAssemblyMarker> Factory(FakeSender sender, bool enabled = true) =>
        new WebApplicationFactory<WebAssemblyMarker>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Testing")
            .UseSetting("ConnectionStrings:Dalan", fixture.ConnectionString)
            .UseSetting("DriverRecovery:Enabled", enabled.ToString())
            .UseSetting("DriverRecovery:PublicBaseUrl", "https://dalan.test")
            .UseSetting("Resend:Enabled", "true")
            .UseSetting("Resend:ApiKey", "test-not-a-real-key")
            .UseSetting("Resend:From", "recovery@dalan.test")
            .UseSetting("Resend:Recipients", "operations@dalan.test")
            .ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(sender);
            }));

    private static async Task Login(HttpClient client, string email, string password)
    {
        using var response = await client.PostAsJsonAsync("/api/driver/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", data.GetProperty("accessToken").GetString());
    }

    private sealed class FakeSender : IEmailSender
    {
        public List<EmailEnvelope> Messages { get; } = [];
        public bool Fail { get; set; }
        public Task<string> SendAsync(EmailEnvelope envelope, CancellationToken cancellationToken)
        {
            if (Fail) throw new HttpRequestException("Simulated email outage.");
            Messages.Add(envelope);
            return Task.FromResult("fake-provider-id");
        }
    }
}
