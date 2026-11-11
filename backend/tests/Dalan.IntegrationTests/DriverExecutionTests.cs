using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dalan.Application.Routing;
using Dalan.Contracts.DriverExecution;
using Dalan.Domain.Deliveries;
using Dalan.Domain.Dispatch;
using Dalan.Domain.Drivers;
using Dalan.Domain.Vehicles;
using Dalan.Infrastructure.Persistence;
using Dalan.Worker;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dalan.IntegrationTests;

[Collection("SQL Server")]
public sealed class DriverExecutionTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Driver_login_scopes_tasks_and_replays_events_without_duplicates()
    {
        var routing = new TestRouting();
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Testing").UseSetting("ConnectionStrings:Dalan", fixture.ConnectionString)
            .ConfigureTestServices(services => services.AddSingleton<IRoutingService>(routing)));
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/driver/tasks")).StatusCode);

        var suffix = Guid.NewGuid().ToString("N")[..10];
        const string password = "Testing-only#1234";
        var email = $"driver-{suffix}@dalan.test";
        var driver = NewDriver("First");
        var otherDriver = NewDriver("Other");
        var vehicle = Vehicle.Register("DR-" + suffix);
        var assignedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var delivery = NewDelivery(driver.Id, "First", assignedAt);
        var otherDelivery = NewDelivery(otherDriver.Id, "Other", assignedAt);
        var route = DispatchRoute.Create(vehicle.Id, driver.Id, 14, 121, 10, 10,
        [
            DispatchStop.Create(delivery.Id, DispatchStopKind.Pickup, 0, 14, 121),
            DispatchStop.Create(delivery.Id, DispatchStopKind.Dropoff, 1, 15, 122)
        ]);

        using (var scope = factory.Services.CreateScope())
        {
            var services = scope.ServiceProvider;
            var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
            var users = services.GetRequiredService<UserManager<IdentityUser>>();
            var db = services.GetRequiredService<DalanDbContext>();
            foreach (var role in new[] { "Driver", "Dispatcher" })
                if (!await roles.RoleExistsAsync(role))
                    Assert.True((await roles.CreateAsync(new IdentityRole(role))).Succeeded);

            var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            Assert.True((await users.CreateAsync(user, password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, "Driver")).Succeeded);
            var staff = new IdentityUser { UserName = "staff-" + email, Email = "staff-" + email, EmailConfirmed = true };
            Assert.True((await users.CreateAsync(staff, password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(staff, "Dispatcher")).Succeeded);

            db.AddRange(vehicle, driver, otherDriver, delivery, otherDelivery,
                DispatchPlan.Create([route], assignedAt),
                new DriverAccount { DriverId = driver.Id, UserId = user.Id });
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/login", new { email, password })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/driver/login", new { email = "staff-" + email, password })).StatusCode);
        await Login("/api/auth/login", "staff-" + email);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/driver/tasks")).StatusCode);
        await Login("/api/driver/login", email);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/deliveries")).StatusCode);

        var tasks = await client.GetFromJsonAsync<DriverTask[]>("/api/driver/tasks");
        var task = Assert.Single(tasks!);
        Assert.Equal(delivery.Id, task.Id);
        Assert.Equal(route.Id, task.RouteId);
        Assert.Equal(1, task.PickupPosition);
        Assert.Equal(2, task.DropoffPosition);
        Assert.Equal(14, task.PickupLatitude);
        Assert.Equal(122, task.DropoffLongitude);

        var ownRoute = $"/api/driver/tasks/{delivery.Id}/route";
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/driver/tasks/{otherDelivery.Id}/route")).StatusCode);
        var roadRoute = await client.GetFromJsonAsync<RoadRoute>(ownRoute);
        Assert.Equal(routing.Result, roadRoute);
        Assert.Equal(new GeoPoint(14, 121), routing.Origin);
        Assert.Equal(new GeoPoint(15, 122), routing.Destination);
        routing.Result = null;
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.GetAsync(ownRoute)).StatusCode);
        routing.Fail = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync(ownRoute)).StatusCode);

        var pickup = new DriverTaskEvent(Guid.NewGuid(), assignedAt, assignedAt.AddMinutes(1),
            "Pickup", null, null, null, null);
        byte[] photo = [0xff, 0xd8, 0xff, 0xe0, 0xff, 0xd9];
        var completion = new DriverTaskEvent(Guid.NewGuid(), assignedAt, assignedAt.AddMinutes(2),
            "Deliver", 15, 122, "Signed by recipient", photo);

        Assert.Equal(HttpStatusCode.NotFound, await Send(otherDelivery.Id, pickup));
        Assert.Equal(HttpStatusCode.Conflict, await Send(delivery.Id, completion));
        Assert.Equal(HttpStatusCode.Conflict, await Send(delivery.Id, pickup with { AssignedAtUtc = assignedAt.AddSeconds(-1) }));
        var simultaneous = await Task.WhenAll(Send(delivery.Id, pickup), Send(delivery.Id, pickup));
        Assert.All(simultaneous, status => Assert.Equal(HttpStatusCode.NoContent, status));
        Assert.Equal(HttpStatusCode.Conflict, await Send(delivery.Id, pickup with { CapturedAtUtc = pickup.CapturedAtUtc.AddSeconds(1) }));
        Assert.Equal(HttpStatusCode.NoContent, await Send(delivery.Id, completion));
        Assert.Equal(HttpStatusCode.NoContent, await Send(delivery.Id, completion));
        Assert.Empty((await client.GetFromJsonAsync<DriverTask[]>("/api/driver/tasks"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(ownRoute)).StatusCode);

        await using var verify = fixture.CreateContext();
        Assert.Equal(DeliveryStatus.Delivered, (await verify.Deliveries.SingleAsync(x => x.Id == delivery.Id)).Status);
        Assert.Equal(2, await verify.DriverEventReceipts.CountAsync(x => x.DeliveryId == delivery.Id));
        Assert.Equal(photo, await verify.DriverEventReceipts.Where(x => x.DeliveryId == delivery.Id && x.Photo != null)
            .Select(x => x.Photo).SingleAsync());
        var retired = await verify.Drivers.SingleAsync(x => x.Id == driver.Id);
        retired.Deactivate();
        await verify.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/driver/tasks")).StatusCode);

        Driver NewDriver(string name) => Driver.Register(name, "09123456789", name + suffix,
            DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1));

        Delivery NewDelivery(Guid owner, string name, DateTimeOffset assigned)
        {
            var item = Delivery.Create(name, "Warehouse", 14, 121, "Destination", 15, 122, assigned.AddMinutes(-1));
            item.Assign(owner, vehicle.Id, assigned);
            return item;
        }

        async Task Login(string endpoint, string accountEmail)
        {
            using var response = await client.PostAsJsonAsync(endpoint, new { email = accountEmail, password });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        }

        async Task<HttpStatusCode> Send(Guid id, DriverTaskEvent request)
        {
            using var response = await client.PostAsJsonAsync($"/api/driver/tasks/{id}/events", request);
            return response.StatusCode;
        }
    }

    [Fact]
    public async Task Recurring_jobs_query_real_data_and_save_one_snapshot_per_interval()
    {
        var now = new DateTimeOffset(2040, 1, 15, 12, 15, 0, TimeSpan.Zero);
        var clock = new FixedTimeProvider(now);
        var delivery = Delivery.Create("Overdue recipient", "Pickup", 14, 121, "Drop-off", 15, 122,
            now.AddDays(-2), expectedDeliveryAt: now.AddDays(-1));
        var vehicle = Vehicle.Register("MAINT-2040", nextMaintenanceDate: new DateOnly(2040, 1, 20));
        await using (var seed = fixture.CreateContext())
        {
            seed.AddRange(delivery, vehicle);
            await seed.SaveChangesAsync();
        }

        await Task.WhenAll(Run("overdue"), Run("overdue"));
        await Run("dispatch");
        await Run("maintenance");
        await Run("maintenance");

        await using var verify = fixture.CreateContext();
        var day = DateOnly.FromDateTime(now.UtcDateTime);
        var records = await verify.OperationsSnapshots.Where(x => x.DayUtc == day).ToListAsync();
        Assert.Equal(3, records.Count);
        Assert.Contains(delivery.Id.ToString(), records.Single(x => x.Kind == "overdue-12").Data);
        Assert.Contains(vehicle.RegistrationNumber, records.Single(x => x.Kind == "maintenance").Data);
        Assert.Equal("[]", records.Single(x => x.Kind == "dispatch").Data);

        async Task Run(string job)
        {
            await using var db = fixture.CreateContext();
            await new OperationsJobs(db, clock, NullLogger<OperationsJobs>.Instance)
                .RunAsync(job, null, CancellationToken.None);
        }
    }

    private sealed class TestRouting : IRoutingService
    {
        public RoadRoute? Result { get; set; } = new(1500, 240, "encoded route");
        public GeoPoint? Origin { get; private set; }
        public GeoPoint? Destination { get; private set; }
        public bool Fail { get; set; }

        public Task<RoadRoute?> GetRouteAsync(GeoPoint origin, GeoPoint destination,
            CancellationToken cancellationToken = default)
        {
            Origin = origin;
            Destination = destination;
            return Fail ? Task.FromException<RoadRoute?>(new HttpRequestException("Provider unavailable"))
                : Task.FromResult(Result);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
