using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Dalan.Application.Routing;
using Dalan.Contracts.DriverExecution;
using Dalan.Domain.Deliveries;
using Dalan.Domain.Drivers;
using Dalan.Domain.Vehicles;
using Dalan.Infrastructure.Persistence;
using Dalan.Infrastructure.Routing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dalan.IntegrationTests;

[Collection("SQL Server")]
public sealed class DriverSecuritySqlTests(SqlServerFixture fixture)
{
    [Theory]
    [InlineData(null)]
    [InlineData("Admin")]
    [InlineData("Dispatcher")]
    [InlineData("Driver")]
    public async Task Real_identity_tokens_enforce_the_role_and_scheme_matrix(string? role)
    {
        using var factory = CreateFactory();
        using var client = Client(factory);
        if (role is not null) await SeedAndLogin(factory, client, role);

        await Expect("/api/vehicles", role is "Admin" or "Dispatcher" ? HttpStatusCode.OK : HttpStatusCode.Unauthorized);
        await Expect("/api/drivers", role == "Admin" ? HttpStatusCode.OK :
            role == "Dispatcher" ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized);
        await Expect("/api/deliveries", role == "Dispatcher" ? HttpStatusCode.OK :
            role == "Admin" ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized);
        await Expect("/api/driver/tasks", role == "Driver" ? HttpStatusCode.OK : HttpStatusCode.Unauthorized);
        await Expect("/api/driver/session", role == "Driver" ? HttpStatusCode.OK : HttpStatusCode.Unauthorized);

        using var mutation = await client.PostAsJsonAsync("/api/vehicles", new { });
        Assert.Equal(role == "Admin" ? HttpStatusCode.BadRequest :
            role == "Dispatcher" ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized, mutation.StatusCode);

        async Task Expect(string path, HttpStatusCode expected)
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(expected, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("missing-photo")]
    [InlineData("invalid-jpeg")]
    [InlineData("missing-gps")]
    [InlineData("invalid-latitude")]
    [InlineData("invalid-longitude")]
    [InlineData("missing-confirmation")]
    [InlineData("long-confirmation")]
    [InlineData("future-time")]
    [InlineData("before-pickup")]
    [InlineData("empty-event-id")]
    [InlineData("unknown-kind")]
    public async Task Invalid_proof_or_event_is_atomic_and_does_not_poison_a_valid_retry(string invalid)
    {
        using var factory = CreateFactory();
        using var client = Client(factory);
        var seeded = await SeedAndLogin(factory, client, "Driver", pickedUp: true);
        var valid = new DriverTaskEvent(Guid.NewGuid(), seeded.AssignedAt, seeded.AssignedAt.AddMinutes(2),
            "Deliver", 14, 121, "Recipient confirmed", [0xff, 0xd8, 0xff, 0xe0, 0xff, 0xd9]);
        var request = invalid switch
        {
            "missing-photo" => valid with { Photo = null },
            "invalid-jpeg" => valid with { Photo = [1, 2, 3, 4, 5, 6] },
            "missing-gps" => valid with { Latitude = null },
            "invalid-latitude" => valid with { Latitude = 91 },
            "invalid-longitude" => valid with { Longitude = -181 },
            "missing-confirmation" => valid with { RecipientConfirmation = " " },
            "long-confirmation" => valid with { RecipientConfirmation = new string('x', 121) },
            "future-time" => valid with { CapturedAtUtc = DateTimeOffset.UtcNow.AddHours(1) },
            "before-pickup" => valid with { CapturedAtUtc = seeded.AssignedAt },
            "empty-event-id" => valid with { EventId = Guid.Empty },
            "unknown-kind" => valid with { Kind = "Cancel" },
            _ => throw new ArgumentOutOfRangeException(nameof(invalid))
        };
        var path = $"/api/driver/tasks/{seeded.DeliveryId}/events";
        using var rejected = await client.PostAsJsonAsync(path, request);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        await using (var db = fixture.CreateContext())
        {
            var delivery = await db.Deliveries.Include(d => d.Proof).SingleAsync(d => d.Id == seeded.DeliveryId);
            Assert.Equal(DeliveryStatus.PickedUp, delivery.Status);
            Assert.Null(delivery.Proof);
            Assert.False(await db.DriverEventReceipts.AnyAsync(r => r.DeliveryId == seeded.DeliveryId));
        }
        using var accepted = await client.PostAsJsonAsync(path, valid);
        using var replay = await client.PostAsJsonAsync(path, valid);
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
        await using var verify = fixture.CreateContext();
        Assert.Equal(DeliveryStatus.Delivered, (await verify.Deliveries.SingleAsync(d => d.Id == seeded.DeliveryId)).Status);
        Assert.Equal(1, await verify.DriverEventReceipts.CountAsync(r => r.DeliveryId == seeded.DeliveryId));
    }

    [Fact]
    public async Task Driver_security_stamp_revokes_existing_token_and_forged_tokens_are_rejected()
    {
        using var factory = CreateFactory();
        using var client = Client(factory);
        var seeded = await SeedAndLogin(factory, client, "Driver");
        using var initial = await client.GetAsync("/api/driver/session");
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = await users.FindByIdAsync(seeded.UserId);
            Assert.True((await users.UpdateSecurityStampAsync(user!)).Succeeded);
        }
        using var revoked = await client.GetAsync("/api/driver/session");
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "forged-invalid-token");
        using var forged = await client.GetAsync("/api/driver/tasks");
        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
    }

    [Theory]
    [InlineData("unreachable", HttpStatusCode.UnprocessableEntity)]
    [InlineData("http-failure", HttpStatusCode.ServiceUnavailable)]
    [InlineData("timeout", HttpStatusCode.ServiceUnavailable)]
    [InlineData("malformed-json", HttpStatusCode.ServiceUnavailable)]
    [InlineData("missing-trip", HttpStatusCode.ServiceUnavailable)]
    [InlineData("invalid-shape", HttpStatusCode.ServiceUnavailable)]
    public async Task Route_provider_failures_preserve_assignment_and_do_not_create_receipts(string failure, HttpStatusCode expected)
    {
        using var handler = new ProviderFailureHandler(failure);
        using var http = new HttpClient(handler) { BaseAddress = new("https://provider.test/") };
        using var factory = CreateFactory(new ValhallaRoutingService(http));
        using var client = Client(factory);
        var seeded = await SeedAndLogin(factory, client, "Driver");
        using var response = await client.GetAsync($"/api/driver/tasks/{seeded.DeliveryId}/route");
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(1, handler.Calls);
        await using var verify = fixture.CreateContext();
        Assert.Equal(DeliveryStatus.Assigned, (await verify.Deliveries.SingleAsync(d => d.Id == seeded.DeliveryId)).Status);
        Assert.False(await verify.DriverEventReceipts.AnyAsync(r => r.DeliveryId == seeded.DeliveryId));
    }

    private WebApplicationFactory<Program> CreateFactory(IRoutingService? routing = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Testing").UseSetting("ConnectionStrings:Dalan", fixture.ConnectionString)
            .ConfigureTestServices(services =>
            {
                if (routing is not null) services.AddSingleton(routing);
            }));

    private static HttpClient Client(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new() { BaseAddress = new("https://localhost") });

    private static async Task<Seeded> SeedAndLogin(WebApplicationFactory<Program> factory, HttpClient client,
        string role, bool pickedUp = false)
    {
        using var scope = factory.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync(role)) Assert.True((await roles.CreateAsync(new IdentityRole(role))).Succeeded);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var suffix = Guid.NewGuid().ToString("N");
        var password = "Test!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)) + "a7";
        var user = new IdentityUser { UserName = suffix + "@dalan.test", Email = suffix + "@dalan.test" };
        Assert.True((await users.CreateAsync(user, password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        var driver = Driver.Register("Security driver", "09123456789", suffix, DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1));
        var vehicle = Vehicle.Register("SEC-" + suffix[..12]);
        var assigned = DateTimeOffset.UtcNow.AddMinutes(-5);
        var delivery = Delivery.Create("Recipient", "Pickup", 14, 121, "Dropoff", 14.1, 121.1, assigned.AddMinutes(-1));
        delivery.Assign(driver.Id, vehicle.Id, assigned);
        if (pickedUp) delivery.MarkPickedUp(driver.Id, assigned.AddMinutes(1));
        var db = scope.ServiceProvider.GetRequiredService<DalanDbContext>();
        db.AddRange(driver, vehicle, delivery);
        if (role == "Driver") db.DriverAccounts.Add(new() { DriverId = driver.Id, UserId = user.Id });
        await db.SaveChangesAsync();
        using var login = await client.PostAsJsonAsync(role == "Driver" ? "/api/driver/login" : "/api/auth/login",
            new { email = user.Email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.GetProperty("accessToken").GetString());
        return new(delivery.Id, user.Id, assigned);
    }

    private sealed record Seeded(Guid DeliveryId, string UserId, DateTimeOffset AssignedAt);

    private sealed class ProviderFailureHandler(string failure) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (failure == "timeout") throw new TaskCanceledException("Simulated provider timeout");
            var status = failure switch
            {
                "unreachable" => HttpStatusCode.BadRequest,
                "http-failure" => HttpStatusCode.ServiceUnavailable,
                _ => HttpStatusCode.OK
            };
            var body = failure switch
            {
                "unreachable" => """{"error_code":442}""",
                "malformed-json" => "{",
                "invalid-shape" => """{"trip":{"summary":{"length":1,"time":60},"legs":[]}}""",
                _ => "{}"
            };
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
