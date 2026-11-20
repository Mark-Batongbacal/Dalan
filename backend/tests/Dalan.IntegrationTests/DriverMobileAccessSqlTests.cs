using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dalan.Domain.Deliveries;
using Dalan.Domain.Drivers;
using Dalan.Domain.Vehicles;
using Dalan.Infrastructure.Identity;
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
public sealed class DriverMobileAccessSqlTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Admin_disable_revokes_existing_and_new_mobile_tokens_without_deleting_delivery_records()
    {
        var seeded = await SeedDriver();
        using var webFactory = WebFactory();
        using var admin = WebClient(webFactory, "Admin");
        using var apiFactory = ApiFactory();
        using var mobile = apiFactory.CreateClient(new() { BaseAddress = new("https://localhost") });
        await Login(mobile, seeded.Email, seeded.Password);

        Assert.Equal(HttpStatusCode.OK, (await mobile.GetAsync("/api/driver/session")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await mobile.GetAsync("/api/driver/tasks")).StatusCode);

        using var firstStatus = await admin.GetAsync($"/api/admin/driver-mobile-access/{seeded.DriverId}");
        Assert.Equal(HttpStatusCode.OK, firstStatus.StatusCode);
        Assert.Equal("Enabled", (await firstStatus.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mobileAccess").GetString());

        await Antiforgery(admin, seeded.DriverId);
        using var disabled = await admin.PostAsJsonAsync($"/api/admin/driver-mobile-access/{seeded.DriverId}/disable",
            new { reason = "Driver account lost the company phone." });
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        Assert.True((await disabled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("changed").GetBoolean());

        Assert.Equal(HttpStatusCode.Unauthorized, (await mobile.GetAsync("/api/driver/session")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await mobile.GetAsync("/api/driver/tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await mobile.PostAsync("/api/driver/tracking/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await mobile.PostAsJsonAsync($"/api/driver/tasks/{seeded.DeliveryId}/events", new
            {
                eventId = Guid.NewGuid(),
                assignedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                capturedAtUtc = DateTimeOffset.UtcNow,
                kind = "Pickup"
            })).StatusCode);

        using var blockedLogin = await mobile.PostAsJsonAsync("/api/driver/login",
            new { email = seeded.Email, password = seeded.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, blockedLogin.StatusCode);

        await using (var db = fixture.CreateContext())
        {
            var user = await db.Users.SingleAsync(x => x.Email == seeded.Email);
            Assert.True(DriverMobileAccess.IsSuspended(user));
            Assert.True(await db.Drivers.AnyAsync(x => x.Id == seeded.DriverId));
            Assert.Equal(DeliveryStatus.Assigned,
                (await db.Deliveries.SingleAsync(x => x.Id == seeded.DeliveryId)).Status);
        }

        using var repeat = await admin.PostAsJsonAsync($"/api/admin/driver-mobile-access/{seeded.DriverId}/disable",
            new { reason = "Still waiting for a safe replacement phone." });
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        Assert.False((await repeat.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("changed").GetBoolean());

        using var enabled = await admin.PostAsJsonAsync($"/api/admin/driver-mobile-access/{seeded.DriverId}/enable",
            new { reason = "Replacement device issued to assigned driver." });
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);

        // Re-enabling does not resurrect the previous token: the Driver must sign in again.
        Assert.Equal(HttpStatusCode.Unauthorized, (await mobile.GetAsync("/api/driver/session")).StatusCode);
        await Login(mobile, seeded.Email, seeded.Password);
        Assert.Equal(HttpStatusCode.OK, (await mobile.GetAsync("/api/driver/session")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await mobile.GetAsync("/api/driver/tasks")).StatusCode);

        await using var verify = fixture.CreateContext();
        Assert.Equal(DeliveryStatus.Assigned,
            (await verify.Deliveries.SingleAsync(x => x.Id == seeded.DeliveryId)).Status);
        var identity = await verify.Users.SingleAsync(x => x.Email == seeded.Email);
        Assert.False(DriverMobileAccess.IsSuspended(identity));
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("Dispatcher", HttpStatusCode.Forbidden)]
    [InlineData("Driver", HttpStatusCode.Forbidden)]
    public async Task Non_admins_cannot_read_or_modify_a_driver_mobile_account(string? role, HttpStatusCode expected)
    {
        var seeded = await SeedDriver();
        using var factory = WebFactory();
        using var client = WebClient(factory, role);
        Assert.Equal(expected,
            (await client.GetAsync($"/api/admin/driver-mobile-access/{seeded.DriverId}")).StatusCode);
        using var result = await client.PostAsJsonAsync(
            $"/api/admin/driver-mobile-access/{seeded.DriverId}/disable",
            new { reason = "Unauthorized account suspension attempt." });
        Assert.Equal(expected, result.StatusCode);

        await using var db = fixture.CreateContext();
        Assert.False(DriverMobileAccess.IsSuspended(await db.Users.SingleAsync(x => x.Email == seeded.Email)));
    }

    [Fact]
    public async Task Admin_actions_require_csrf_and_a_reason()
    {
        var seeded = await SeedDriver();
        using var factory = WebFactory();
        using var client = WebClient(factory, "Admin");
        using var noToken = await client.PostAsJsonAsync(
            $"/api/admin/driver-mobile-access/{seeded.DriverId}/disable",
            new { reason = "A valid reason with no antiforgery token." });
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);

        await Antiforgery(client, seeded.DriverId);
        using var shortReason = await client.PostAsJsonAsync(
            $"/api/admin/driver-mobile-access/{seeded.DriverId}/disable", new { reason = "no" });
        Assert.Equal(HttpStatusCode.BadRequest, shortReason.StatusCode);
        await using var db = fixture.CreateContext();
        Assert.False(DriverMobileAccess.IsSuspended(await db.Users.SingleAsync(x => x.Email == seeded.Email)));
    }

    [Fact]
    public async Task Unlinked_driver_is_not_given_an_account_by_revocation_request()
    {
        var driver = Driver.Register("Unlinked", "09123456789", Guid.NewGuid().ToString("N"),
            DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1));
        await using (var db = fixture.CreateContext())
        {
            db.Drivers.Add(driver);
            await db.SaveChangesAsync();
        }
        using var factory = WebFactory();
        using var admin = WebClient(factory, "Admin");
        await Antiforgery(admin, driver.Id);
        using var response = await admin.PostAsJsonAsync(
            $"/api/admin/driver-mobile-access/{driver.Id}/disable",
            new { reason = "This driver does not have a mobile account." });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var verify = fixture.CreateContext();
        Assert.False(await verify.DriverAccounts.AnyAsync(x => x.DriverId == driver.Id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GPS_exception_is_pending_until_explicit_staff_review_and_preserves_missing_fix(bool approve)
    {
        var seeded = await SeedDriver(); DateTimeOffset assigned;
        await using (var db = fixture.CreateContext())
        {
            var delivery = await db.Deliveries.SingleAsync(x => x.Id == seeded.DeliveryId);
            assigned = delivery.AssignedAtUtc!.Value;
            delivery.MarkPickedUp(seeded.DriverId, assigned.AddMinutes(1)); await db.SaveChangesAsync();
        }
        using var api = ApiFactory().WithWebHostBuilder(b => b.UseSetting("DriverGpsProof:Enabled", "true"));
        using var mobile = api.CreateClient(new() { BaseAddress = new("https://localhost") });
        await Login(mobile, seeded.Email, seeded.Password);
        var proof = new Dalan.Contracts.DriverExecution.DriverTaskEvent(Guid.NewGuid(), assigned,
            assigned.AddMinutes(2), "Deliver", null, null, "Recipient", [255, 216, 255, 224, 255, 217]);
        var endpoint = $"/api/driver/tasks/{seeded.DeliveryId}/gps-exceptions";
        var payload = new Dalan.Contracts.DriverExecution.GpsExceptionRequest(proof, "Location unavailable at the gate");
        Assert.Equal(HttpStatusCode.Accepted, (await mobile.PostAsJsonAsync(endpoint, payload)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await mobile.PostAsJsonAsync(endpoint, payload)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await mobile.PostAsJsonAsync(endpoint, payload with { Reason = "Changed evidence must not overwrite" })).StatusCode);
        await using (var db = fixture.CreateContext())
        {
            Assert.Equal(Dalan.Domain.Deliveries.DeliveryStatus.PickedUp, (await db.Deliveries.FindAsync(seeded.DeliveryId))!.Status);
            Assert.False(await db.DriverEventReceipts.AnyAsync(x => x.EventId == proof.EventId));
        }
        using var web = WebFactory().WithWebHostBuilder(b => b.UseSetting("DriverGpsProof:Enabled", "true"));
        using var driver = WebClient(web, "Driver");
        Assert.Equal(HttpStatusCode.Forbidden, (await driver.GetAsync("/api/gps-exceptions")).StatusCode);
        using var admin = WebClient(web, "Admin"); await Antiforgery(admin, seeded.DriverId);
        var reviewUrl = $"/api/gps-exceptions/{proof.EventId}/review";
        var decision = new { approve, reason = "Reviewed photo and dispatcher evidence" };
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync(reviewUrl, decision)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(reviewUrl, decision)).StatusCode);
        Assert.Equal(approve ? HttpStatusCode.NoContent : HttpStatusCode.UnprocessableEntity, (await mobile.PostAsJsonAsync(endpoint, payload)).StatusCode);
        await using var verify = fixture.CreateContext();
        var saved = await verify.Deliveries.Include(x => x.Proof).SingleAsync(x => x.Id == seeded.DeliveryId);
        Assert.Equal(approve ? Dalan.Domain.Deliveries.DeliveryStatus.Delivered : Dalan.Domain.Deliveries.DeliveryStatus.PickedUp, saved.Status);
        if (approve) { Assert.NotNull(saved.Proof); Assert.Null(saved.Proof.Latitude); Assert.Null(saved.Proof.Longitude); }
        Assert.Equal(approve ? 1 : 0, await verify.DriverEventReceipts.CountAsync(x => x.EventId == proof.EventId));
        var history = await verify.DriverGpsExceptions.SingleAsync(x => x.Id == proof.EventId);
        Assert.Equal("access-test-admin", history.ReviewerUserId); Assert.NotNull(history.ReviewedAtUtc);
    }

    private async Task<(Guid DriverId, Guid DeliveryId, string Email, string Password)> SeedDriver()
    {
        var random = Guid.NewGuid().ToString("N");
        var driver = Driver.Register("Revocation Driver", "09123456789", random,
            DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1));
        var vehicle = Vehicle.Register("REV-" + random[..12]);
        var email = "revocation-" + random + "@dalan.test";
        var password = "Driver-Auth#" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        var assigned = DateTimeOffset.UtcNow.AddMinutes(-5);
        var delivery = Delivery.Create("Recipient", "Warehouse", 14, 121, "Destination", 14.1, 121.1,
            assigned.AddMinutes(-1));
        delivery.Assign(driver.Id, vehicle.Id, assigned);
        using (var api = ApiFactory())
        using (var scope = api.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var db = scope.ServiceProvider.GetRequiredService<DalanDbContext>();
            if (!await roles.RoleExistsAsync("Driver"))
                Assert.True((await roles.CreateAsync(new IdentityRole("Driver"))).Succeeded);
            var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            Assert.True((await users.CreateAsync(user, password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, "Driver")).Succeeded);
            db.AddRange(driver, vehicle, delivery, new DriverAccount { DriverId = driver.Id, UserId = user.Id });
            await db.SaveChangesAsync();
        }
        return (driver.Id, delivery.Id, email, password);
    }

    private WebApplicationFactory<WebAssemblyMarker> WebFactory() =>
        new WebApplicationFactory<WebAssemblyMarker>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Testing")
            .UseSetting("ConnectionStrings:Dalan", fixture.ConnectionString)
            .ConfigureTestServices(services =>
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "AccessTest";
                    options.DefaultChallengeScheme = "AccessTest";
                    options.DefaultForbidScheme = "AccessTest";
                }).AddScheme<AuthenticationSchemeOptions, RoleHandler>("AccessTest", _ => { })));

    private WebApplicationFactory<Program> ApiFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Testing")
            .UseSetting("ConnectionStrings:Dalan", fixture.ConnectionString));

    private static HttpClient WebClient(WebApplicationFactory<WebAssemblyMarker> factory, string? role)
    {
        var client = factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        if (role is not null) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private static async Task Antiforgery(HttpClient client, Guid driverId)
    {
        var html = await client.GetStringAsync("/Drivers/Edit/" + driverId);
        var match = Regex.Match(html, @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""");
        Assert.True(match.Success);
        client.DefaultRequestHeaders.Remove("RequestVerificationToken");
        client.DefaultRequestHeaders.Add("RequestVerificationToken",
            WebUtility.HtmlDecode(match.Groups[1].Value));
    }

    private static async Task Login(HttpClient client, string email, string password)
    {
        using var response = await client.PostAsJsonAsync("/api/driver/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token.GetProperty("accessToken").GetString());
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
                new Claim(ClaimTypes.NameIdentifier, "access-test-admin"),
                new Claim(ClaimTypes.Role, role.ToString())
            ], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
