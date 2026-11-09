using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Dalan.Application.Dispatch;
using Dalan.Application.FleetOptimization;
using Dalan.Application.Routing;
using Dalan.Contracts.Deliveries;
using Dalan.Contracts.Dispatch;
using Dalan.Contracts.FleetOptimization;
using Dalan.Domain.Deliveries;
using Dalan.Domain.Drivers;
using Dalan.Domain.Vehicles;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
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
public sealed class DispatchHttpSqlTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Dispatcher_http_workflow_persists_one_plan()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var vehicle = Vehicle.Register("HTTP-" + suffix, "Van", 100);
        var driver = Driver.Register(
            "HTTP Dispatch Driver", "09123456789", "HTTP-LIC-" + suffix,
            DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1));
        driver.AssignVehicle(vehicle.Id);

        var delivery = Delivery.Create(
            "HTTP Recipient", "Warehouse", 7.1, 125.6,
            "Destination", 7.2, 125.7,
            DateTimeOffset.UtcNow.AddMinutes(-5), 10);

        await using (var seed = fixture.CreateContext())
        {
            seed.Vehicles.Add(vehicle);
            seed.Drivers.Add(driver);
            seed.Deliveries.Add(delivery);
            await seed.SaveChangesAsync();
        }

        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting(
                    "ConnectionStrings:Dalan", fixture.ConnectionString);
                builder.UseSetting("Valhalla:BaseUrl", "https://unused.test/");

                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ITravelMatrixService>();
                    services.AddSingleton<ITravelMatrixService>(
                        new LineMatrix());

                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = "Test";
                        options.DefaultChallengeScheme = "Test";
                        options.DefaultForbidScheme = "Test";
                    }).AddScheme<
                        AuthenticationSchemeOptions,
                        RoleHeaderHandler>("Test", _ => { });
                });
            });

        using var client = factory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add(
            "X-Test-Role", "Dispatcher");

        var vehicles = new[]
        {
            new DispatchVehicleStartRequest(
                vehicle.Id, new CoordinateRequest(7.0, 125.6))
        };

        using var previewResponse = await client.PostAsJsonAsync(
            "/api/dispatch-plans/preview",
            new PreviewDispatchRequest(vehicles, [delivery.Id]));

        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = await previewResponse.Content
            .ReadFromJsonAsync<DispatchPreviewResponse>();
        Assert.NotNull(preview);
        Assert.Empty(preview.UnassignedDeliveryIds);
        Assert.Single(preview.Routes);

        using var confirmResponse = await client.PostAsJsonAsync(
            "/api/dispatch-plans",
            new ConfirmDispatchRequest(
                vehicles,
                [
                    new DispatchReviewedRouteRequest(
                        vehicle.Id,
                        [
                            new(delivery.Id, "Pickup"),
                            new(delivery.Id, "Dropoff")
                        ])
                ],
                preview.DeliveryVersions));

        Assert.Equal(HttpStatusCode.Created, confirmResponse.StatusCode);
        var confirmed = await confirmResponse.Content
            .ReadFromJsonAsync<DispatchPlanResponse>();
        Assert.NotNull(confirmed);
        Assert.Equal(vehicle.Id, Assert.Single(confirmed.Routes).VehicleId);

        using var getResponse = await client.GetAsync(
            confirmResponse.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        await using (var verify = fixture.CreateContext())
        {
            var saved = await verify.DispatchPlans.AsNoTracking()
                .Include(plan => plan.Routes)
                .ThenInclude(route => route.Stops)
                .SingleAsync(plan => plan.Id == confirmed.Id);

            Assert.Equal(2, Assert.Single(saved.Routes).Stops.Count);

            var assigned = await verify.Deliveries.AsNoTracking()
                .SingleAsync(item => item.Id == delivery.Id);
            Assert.Equal(DeliveryStatus.Assigned, assigned.Status);
            Assert.Equal(driver.Id, assigned.DriverId);
            Assert.Equal(vehicle.Id, assigned.VehicleId);
        }

        using var duplicate = await client.PostAsJsonAsync(
            "/api/dispatch-plans",
            new ConfirmDispatchRequest(
                vehicles,
                [
                    new DispatchReviewedRouteRequest(
                        vehicle.Id,
                        [
                            new(delivery.Id, "Pickup"),
                            new(delivery.Id, "Dropoff")
                        ])
                ],
                preview.DeliveryVersions));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        using var reassignment = await client.PostAsJsonAsync(
            $"/api/deliveries/{delivery.Id}/assign",
            new AssignDeliveryRequest(driver.Id, vehicle.Id));
        Assert.Equal(HttpStatusCode.Conflict, reassignment.StatusCode);
    }

    private sealed class LineMatrix : ITravelMatrixService
    {
        public Task<TravelMetric?[,]> GetMatrixAsync(
            IReadOnlyList<GeoPoint> points,
            CancellationToken cancellationToken = default)
        {
            var result = new TravelMetric?[points.Count, points.Count];

            for (var from = 0; from < points.Count; from++)
                for (var to = 0; to < points.Count; to++)
                {
                    var distance = Math.Abs(
                        points[from].Latitude - points[to].Latitude);
                    result[from, to] = new TravelMetric(
                        distance * 1_000, distance * 60);
                }

            return Task.FromResult(result);
        }
    }

    private sealed class RoleHeaderHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(
            options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(
                    "X-Test-Role", out var role))
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "dispatch-http-sql"),
                    new Claim(ClaimTypes.Role, role.ToString())
                ],
                Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(
                    new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
