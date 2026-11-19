using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Dalan.Application.Notifications;
using Dalan.Domain.Drivers;
using Dalan.Infrastructure.Persistence;
using Dalan.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dalan.IntegrationTests;

[Collection("SQL Server")]
public sealed class DriverInvitationWebTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Admin_invitation_creates_passwordless_linked_account_then_driver_sets_own_password_once()
    {
        var driverId = await RegisterDriver();
        var email = "invite-" + Guid.NewGuid().ToString("N") + "@dalan.test";
        var sender = new FakeSender();
        using var factory = Factory(sender);
        using var admin = Client(factory, "Admin");
        await Antiforgery(admin, driverId);
        Assert.Equal(HttpStatusCode.Redirect, (await admin.GetAsync("/DriverAccounts/Create?id=" + driverId)).StatusCode);

        using var issued = await admin.PostAsJsonAsync("/api/driver-invitations/issue", new { driverId, email });
        Assert.Equal(HttpStatusCode.Accepted, issued.StatusCode);
        Assert.True(issued.Headers.CacheControl?.NoStore == true);
        var envelope = Assert.Single(sender.Messages);
        Assert.Equal(email, Assert.Single(envelope.To));
        Assert.DoesNotContain("Password=", envelope.Text, StringComparison.OrdinalIgnoreCase);
        var token = Token(envelope.Text);

        await using (var db = fixture.CreateContext())
        {
            var account = await db.DriverAccounts.SingleAsync(x => x.DriverId == driverId);
            var user = await db.Users.SingleAsync(x => x.Id == account.UserId);
            Assert.Null(user.PasswordHash);
            Assert.False(user.EmailConfirmed);
        }

        const string password = "Driver-invite#Strong2026";
        using var wrongEmail = await admin.PostAsJsonAsync("/api/driver-invitations/activate",
            new { email = "other-" + email, token, password });
        Assert.Equal(HttpStatusCode.BadRequest, wrongEmail.StatusCode);

        using var activated = await admin.PostAsJsonAsync("/api/driver-invitations/activate",
            new { email, token, password });
        Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
        using var replay = await admin.PostAsJsonAsync("/api/driver-invitations/activate",
            new { email, token, password = "Alternate-Driver#2026" });
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);

        await using (var db = fixture.CreateContext())
        {
            var account = await db.DriverAccounts.SingleAsync(x => x.DriverId == driverId);
            var user = await db.Users.SingleAsync(x => x.Id == account.UserId);
            Assert.True(user.EmailConfirmed);
            Assert.NotNull(user.PasswordHash);
            Assert.Single(await db.DriverAccounts.Where(x => x.DriverId == driverId).ToListAsync());
        }

        // The existing DriverBearer endpoint works with the password the Driver chose.
        using var apiFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Testing").UseSetting("ConnectionStrings:Dalan", fixture.ConnectionString));
        using var apiClient = apiFactory.CreateClient(new() { BaseAddress = new("https://localhost") });
        using var mobileLogin = await apiClient.PostAsJsonAsync("/api/driver/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, mobileLogin.StatusCode);

        using var reissue = await admin.PostAsJsonAsync("/api/driver-invitations/issue", new { driverId, email });
        Assert.Equal(HttpStatusCode.Conflict, reissue.StatusCode);
        Assert.Single(sender.Messages);
    }

    [Fact]
    public async Task Reissue_rotates_security_stamp_and_invalidates_previous_token()
    {
        var driverId = await RegisterDriver();
        var email = "reissue-" + Guid.NewGuid().ToString("N") + "@dalan.test";
        var sender = new FakeSender();
        using var factory = Factory(sender);
        using var admin = Client(factory, "Admin");
        await Antiforgery(admin, driverId);

        using var first = await admin.PostAsJsonAsync("/api/driver-invitations/issue", new { driverId, email });
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var oldToken = Token(sender.Messages.Single().Text);
        using var second = await admin.PostAsJsonAsync("/api/driver-invitations/issue", new { driverId, email });
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(2, sender.Messages.Count);
        var currentToken = Token(sender.Messages[1].Text);
        Assert.NotEqual(oldToken, currentToken);

        const string password = "Reissued-invite#2026";
        using var expired = await admin.PostAsJsonAsync("/api/driver-invitations/activate",
            new { email, token = oldToken, password });
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
        using var accepted = await admin.PostAsJsonAsync("/api/driver-invitations/activate",
            new { email, token = currentToken, password });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("Dispatcher", HttpStatusCode.Forbidden)]
    [InlineData("Driver", HttpStatusCode.Forbidden)]
    public async Task Only_admin_can_issue_driver_invitations(string? role, HttpStatusCode expected)
    {
        var sender = new FakeSender();
        using var factory = Factory(sender);
        using var client = Client(factory, role);
        using var response = await client.PostAsJsonAsync("/api/driver-invitations/issue",
            new { driverId = Guid.NewGuid(), email = "any@dalan.test" });
        Assert.Equal(expected, response.StatusCode);
        Assert.Empty(sender.Messages);
    }

    [Fact]
    public async Task Issuing_requires_antiforgery_and_feature_configuration()
    {
        var driverId = await RegisterDriver();
        var email = "config-" + Guid.NewGuid().ToString("N") + "@dalan.test";
        var sender = new FakeSender();
        using var configured = Factory(sender);
        using var admin = Client(configured, "Admin");
        using var blocked = await admin.PostAsJsonAsync("/api/driver-invitations/issue", new { driverId, email });
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);

        using var disabled = Factory(sender, enabled: false);
        using var disabledAdmin = Client(disabled, "Admin");
        await Antiforgery(disabledAdmin, driverId);
        using var unavailable = await disabledAdmin.PostAsJsonAsync("/api/driver-invitations/issue",
            new { driverId, email });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        await using var db = fixture.CreateContext();
        Assert.False(await db.Users.AnyAsync(x => x.Email == email));
        Assert.Empty(sender.Messages);
    }

    [Fact]
    public async Task Email_failure_keeps_account_passwordless_and_allows_admin_retry()
    {
        var driverId = await RegisterDriver();
        var email = "retry-" + Guid.NewGuid().ToString("N") + "@dalan.test";
        var sender = new FakeSender { Fail = true };
        using var factory = Factory(sender);
        using var admin = Client(factory, "Admin");
        await Antiforgery(admin, driverId);
        using var fail = await admin.PostAsJsonAsync("/api/driver-invitations/issue", new { driverId, email });
        Assert.Equal(HttpStatusCode.BadGateway, fail.StatusCode);
        await using (var db = fixture.CreateContext())
        {
            var account = await db.DriverAccounts.SingleAsync(x => x.DriverId == driverId);
            Assert.Null((await db.Users.SingleAsync(x => x.Id == account.UserId)).PasswordHash);
        }
        sender.Fail = false;
        using var retry = await admin.PostAsJsonAsync("/api/driver-invitations/issue", new { driverId, email });
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        Assert.Single(sender.Messages);
    }

    private async Task<Guid> RegisterDriver()
    {
        var key = Guid.NewGuid().ToString("N");
        var driver = Driver.Register("Invited Driver", "09123456789", key,
            DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1));
        await using var db = fixture.CreateContext();
        db.Drivers.Add(driver);
        await db.SaveChangesAsync();
        return driver.Id;
    }

    private static string Token(string text)
    {
        var line = text.Split('\n').Single(x => x.StartsWith("https://", StringComparison.Ordinal));
        var fragment = new Uri(line).Fragment.TrimStart('#');
        var raw = fragment.Split('&').Single(x => x.StartsWith("token=", StringComparison.Ordinal));
        return Uri.UnescapeDataString(raw["token=".Length..]);
    }

    private static async Task Antiforgery(HttpClient client, Guid driverId)
    {
        var page = await client.GetStringAsync("/Drivers/" + driverId + "/Access");
        var match = Regex.Match(page, @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""");
        Assert.True(match.Success, "The Driver account form should include a CSRF token.");
        client.DefaultRequestHeaders.Remove("RequestVerificationToken");
        client.DefaultRequestHeaders.Add("RequestVerificationToken",
            WebUtility.HtmlDecode(match.Groups[1].Value));
    }

    private WebApplicationFactory<WebAssemblyMarker> Factory(FakeSender sender, bool enabled = true) =>
        new WebApplicationFactory<WebAssemblyMarker>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Testing")
            .UseSetting("ConnectionStrings:Dalan", fixture.ConnectionString)
            .UseSetting("DriverInvitations:Enabled", enabled.ToString())
            .UseSetting("DriverInvitations:PublicBaseUrl", "https://dalan.test")
            .UseSetting("Resend:Enabled", "true")
            .UseSetting("Resend:ApiKey", "fake-test-key")
            .UseSetting("Resend:From", "no-reply@dalan.test")
            .UseSetting("Resend:Recipients", "operations@dalan.test")
            .ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(sender);
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "InvitationTest";
                    options.DefaultChallengeScheme = "InvitationTest";
                    options.DefaultForbidScheme = "InvitationTest";
                }).AddScheme<AuthenticationSchemeOptions, RoleHandler>("InvitationTest", _ => { });
            }));

    private static HttpClient Client(WebApplicationFactory<WebAssemblyMarker> factory, string? role)
    {
        var client = factory.CreateClient(new()
        {
            BaseAddress = new("https://localhost"),
            AllowAutoRedirect = false
        });
        if (role is not null) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private sealed class FakeSender : IEmailSender
    {
        public List<EmailEnvelope> Messages { get; } = [];
        public bool Fail { get; set; }
        public Task<string> SendAsync(EmailEnvelope message, CancellationToken cancellationToken)
        {
            if (Fail) throw new HttpRequestException("Simulated Resend outage.");
            Messages.Add(message);
            return Task.FromResult("test-accepted");
        }
    }

    private sealed class RoleHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role))
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "test-admin"),
                 new Claim(ClaimTypes.Role, role.ToString())], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
