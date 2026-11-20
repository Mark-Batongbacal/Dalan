using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dalan.IntegrationTests;

[Collection("SQL Server")]
public sealed class AccountAccessAuditSqlTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Audit_cursor_does_not_drop_events_with_the_same_timestamp()
    {
        var driverId = Guid.NewGuid(); var at = DateTimeOffset.UtcNow;
        await using (var db = fixture.CreateContext())
        {
            for (var i = 0; i < 6; i++) db.AccountAccessAudits.Add(new() { DriverId = driverId, OccurredAtUtc = at, Action = "CursorFixture", Outcome = "Succeeded" });
            await db.SaveChangesAsync();
        }
        using var factory = WebFactory(); using var admin = WebClient(factory, "Admin");
        var ids = new HashSet<Guid>(); string? before = null; Guid? cursor = null;
        for (var page = 0; page < 3; page++)
        {
            var url = $"/api/admin/account-audit?driverId={driverId}&take=2";
            if (before is not null) url += "&beforeUtc=" + Uri.EscapeDataString(before) + "&beforeId=" + cursor;
            var body = await admin.GetFromJsonAsync<JsonElement>(url); var items = body.EnumerateArray().ToArray(); Assert.Equal(2, items.Length);
            foreach (var item in items) Assert.True(ids.Add(item.GetProperty("id").GetGuid()));
            before = items[^1].GetProperty("occurredAtUtc").GetString(); cursor = items[^1].GetProperty("id").GetGuid();
        }
        Assert.Equal(6, ids.Count);
    }

    [Fact]
    public async Task Audit_write_failure_rolls_back_account_creation_instead_of_reporting_success()
    {
        var driver = Driver.Register("Atomic audit fixture", "09123456789", Guid.NewGuid().ToString("N"), DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1));
        await using (var db = fixture.CreateContext()) { db.Drivers.Add(driver); await db.SaveChangesAsync(); }
        using var web = WebFactory().WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            services.AddDbContext<DalanDbContext>(options => options.AddInterceptors(new RejectAuditWrite()))));
        using var admin = WebClient(web, "Admin");
        var csrf = Token(await admin.GetStringAsync("/DriverAccounts/Create?id=" + driver.Id));
        var email = Guid.NewGuid().ToString("N") + "@dalan.test";
        using var response = await admin.PostAsync("/DriverAccounts/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["DriverId"] = driver.Id.ToString(),
            ["Email"] = email,
            ["Password"] = "Atomic-Password!2026",
            ["__RequestVerificationToken"] = csrf
        }));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await using var verify = fixture.CreateContext();
        Assert.False(await verify.DriverAccounts.AnyAsync(x => x.DriverId == driver.Id));
        Assert.False(await verify.Users.AnyAsync(x => x.Email == email));
    }
    private sealed class RejectAuditWrite : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData data, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken token = default)
        {
            if (data.Context!.ChangeTracker.Entries<AccountAccessAudit>().Any(x => x.State == EntityState.Added))
                throw new InvalidOperationException("Injected audit persistence failure");
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task Admin_creation_persists_identifiers_without_password_or_email_in_audit()
    {
        var driver = Driver.Register("Audit driver", "09123456789", Guid.NewGuid().ToString("N"),
            DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1));
        await using (var db = fixture.CreateContext())
        {
            db.Drivers.Add(driver);
            await db.SaveChangesAsync();
        }

        var email = "audited-" + Guid.NewGuid().ToString("N") + "@dalan.test";
        var password = "SecretPassword#" + Convert.ToHexString(RandomNumberGenerator.GetBytes(10));
        using var factory = WebFactory();
        using var admin = WebClient(factory, "Admin");
        var form = await admin.GetStringAsync("/DriverAccounts/Create?id=" + driver.Id);
        var token = Token(form);
        using var response = await admin.PostAsync("/DriverAccounts/Create",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["DriverId"] = driver.Id.ToString(),
                ["Email"] = email,
                ["Password"] = password
            }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        await using var verify = fixture.CreateContext();
        var audit = await verify.AccountAccessAudits
            .SingleAsync(x => x.DriverId == driver.Id && x.Action == "DriverAccountCreated");
        Assert.Equal("Succeeded", audit.Outcome);
        Assert.Equal("audit-admin", audit.ActorUserId);
        Assert.NotNull(audit.TargetUserId);
        Assert.NotNull(audit.CorrelationId);
        var serialized = JsonSerializer.Serialize(audit);
        Assert.DoesNotContain(password, serialized);
        Assert.DoesNotContain(email, serialized);
        Assert.True(await verify.DriverAccounts.AnyAsync(x => x.DriverId == driver.Id));
    }

    [Fact]
    public async Task Driver_login_records_success_and_known_account_failure_without_credentials()
    {
        var email = "login-audit-" + Guid.NewGuid().ToString("N") + "@dalan.test";
        var password = "CorrectPassword#" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        string userId;
        using (var api = ApiFactory())
        using (var scope = api.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            if (!await roles.RoleExistsAsync("Driver"))
                Assert.True((await roles.CreateAsync(new IdentityRole("Driver"))).Succeeded);
            var identity = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            Assert.True((await users.CreateAsync(identity, password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(identity, "Driver")).Succeeded);
            userId = identity.Id;
            var driver = Driver.Register("Audit login", "09123456789", Guid.NewGuid().ToString("N"),
                DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1));
            var db = scope.ServiceProvider.GetRequiredService<DalanDbContext>();
            db.AddRange(driver, new DriverAccount { DriverId = driver.Id, UserId = userId });
            await db.SaveChangesAsync();
        }

        using var factory = ApiFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new("https://localhost") });
        using var failed = await client.PostAsJsonAsync("/api/driver/login",
            new { email, password = "Wrong-" + password });
        Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        using var accepted = await client.PostAsJsonAsync("/api/driver/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        await using var verify = fixture.CreateContext();
        var events = await verify.AccountAccessAudits
            .Where(x => x.TargetUserId == userId && x.Action == "DriverLogin").ToListAsync();
        Assert.Contains(events, x => x.Outcome == "Rejected");
        Assert.Contains(events, x => x.Outcome == "Succeeded");
        var serialized = JsonSerializer.Serialize(events);
        Assert.DoesNotContain(email, serialized);
        Assert.DoesNotContain(password, serialized);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("Dispatcher", HttpStatusCode.Forbidden)]
    [InlineData("Driver", HttpStatusCode.Forbidden)]
    [InlineData("Admin", HttpStatusCode.OK)]
    public async Task Audit_history_is_admin_only(string? role, HttpStatusCode expected)
    {
        using var factory = WebFactory();
        using var client = WebClient(factory, role);
        using var response = await client.GetAsync("/api/admin/account-audit?take=5");
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            Assert.True(response.Headers.CacheControl?.NoStore == true);
            Assert.Equal(JsonValueKind.Array, (await response.Content.ReadFromJsonAsync<JsonElement>()).ValueKind);
        }
    }

    [Fact]
    public async Task Disabled_audit_does_not_expose_history_or_write_new_events()
    {
        using var factory = WebFactory(enabled: false);
        using var admin = WebClient(factory, "Admin");
        using var response = await admin.GetAsync("/api/admin/account-audit?take=10");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private WebApplicationFactory<WebAssemblyMarker> WebFactory(bool enabled = true) =>
        new WebApplicationFactory<WebAssemblyMarker>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Testing")
            .UseSetting("ConnectionStrings:Dalan", fixture.ConnectionString)
            .UseSetting("AccountAudit:Enabled", enabled.ToString())
            .ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "AuditTest";
                    options.DefaultChallengeScheme = "AuditTest";
                    options.DefaultForbidScheme = "AuditTest";
                }).AddScheme<AuthenticationSchemeOptions, RoleHandler>("AuditTest", _ => { });
            }));

    private WebApplicationFactory<Program> ApiFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Testing")
            .UseSetting("ConnectionStrings:Dalan", fixture.ConnectionString)
            .UseSetting("AccountAudit:Enabled", "true"));

    private static HttpClient WebClient(WebApplicationFactory<WebAssemblyMarker> factory, string? role)
    {
        var client = factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        if (role is not null) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private static string Token(string html)
    {
        var match = Regex.Match(html, @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""");
        Assert.True(match.Success);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private sealed class RoleHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role))
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "audit-admin"),
                new Claim(ClaimTypes.Role, role.ToString())
            ], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
