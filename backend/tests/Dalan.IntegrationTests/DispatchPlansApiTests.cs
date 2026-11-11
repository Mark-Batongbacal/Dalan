using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Dalan.Application.Dispatch;
using Dalan.Application.Routing;
using Dalan.Contracts.Dispatch;
using Dalan.Contracts.FleetOptimization;
using Dalan.Domain.Dispatch;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dalan.IntegrationTests;

public sealed class DispatchPlansApiTests
{
    [Fact]
    public async Task Omitted_strategy_keeps_legacy_default_and_explicit_selection_is_returned()
    {
        var service = new FakeDispatchService();
        using var factory = CreateFactory(service);
        using var client = CreateClient(factory, "Dispatcher");
        var legacy = new { PreviewBody().Vehicles, PreviewBody().DeliveryIds };
        using var response = await client.PostAsJsonAsync("/api/dispatch-plans/preview", legacy);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(DispatchStrategy.MinimumTotalTravelTime, service.LastPreview!.Strategy);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"strategy\":\"MinimumTotalTravelTime\"", json);

        using var selected = await client.PostAsJsonAsync("/api/dispatch-plans/preview", PreviewBody());
        Assert.Equal(HttpStatusCode.OK, selected.StatusCode);
    }

    [Theory]
    [InlineData("\"Unknown\"")]
    [InlineData("\"WorkloadEquity, EarliestFleetCompletion\"")]
    [InlineData("\"0\"")]
    [InlineData("0")]
    [InlineData("99")]
    [InlineData("null")]
    public async Task Invalid_strategy_never_reaches_preview_or_confirmation(string value)
    {
        var service = new FakeDispatchService();
        using var factory = CreateFactory(service);
        using var client = CreateClient(factory, "Dispatcher");
        var json = System.Text.Json.JsonSerializer.Serialize(PreviewBody());
        json = json.Replace("\"Strategy\":\"MinimumTotalTravelTime\"", "\"Strategy\":" + value);
        using var response = await client.PostAsync("/api/dispatch-plans/preview",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(service.LastPreview);

        json = System.Text.Json.JsonSerializer.Serialize(ConfirmBody());
        json = json.Replace("\"Strategy\":\"MinimumTotalTravelTime\"", "\"Strategy\":" + value);
        using var confirm = await client.PostAsync("/api/dispatch-plans",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, confirm.StatusCode);
        Assert.Null(service.LastConfirm);
    }

    [Theory]
    [InlineData(DispatchStrategy.WorkloadEquity)]
    [InlineData(DispatchStrategy.WarehouseClusterRotation)]
    [InlineData(DispatchStrategy.EarliestFleetCompletion)]
    public async Task Selected_strategy_and_settings_reach_preview_and_reviewed_confirmation(DispatchStrategy strategy)
    {
        var service = new FakeDispatchService();
        using var factory = CreateFactory(service);
        using var client = CreateClient(factory, "Dispatcher");
        var settings = new DispatchStrategySettings(new(7, 125.6), 120, 7200);
        using var response = await client.PostAsJsonAsync("/api/dispatch-plans/preview",
            PreviewBody() with { Strategy = strategy, Settings = settings });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.Content.ReadFromJsonAsync<DispatchPreviewResponse>();
        Assert.Equal(strategy, preview!.Strategy);
        Assert.Equal(settings, service.LastPreview!.Settings);
        using var confirm = await client.PostAsJsonAsync("/api/dispatch-plans",
            ConfirmBody() with { Strategy = strategy, Settings = settings, RotationVersion = 12 });
        Assert.Equal(HttpStatusCode.Created, confirm.StatusCode);
        Assert.Equal(strategy, service.LastConfirm!.Strategy);
        Assert.Equal(settings, service.LastConfirm.Settings);
        Assert.Equal(12, service.LastConfirm.RotationVersion);
    }

    private static readonly Guid VehicleId = Guid.NewGuid();
    private static readonly Guid DriverId = Guid.NewGuid();
    private static readonly Guid DeliveryId = Guid.NewGuid();
    private static readonly Guid UnassignedId = Guid.NewGuid();

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized,
        HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized)]
    [InlineData("Admin", HttpStatusCode.Forbidden,
        HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    [InlineData("Dispatcher", HttpStatusCode.OK,
        HttpStatusCode.Created, HttpStatusCode.OK)]
    public async Task Dispatch_routes_require_dispatcher(
        string? role,
        HttpStatusCode previewStatus,
        HttpStatusCode confirmStatus,
        HttpStatusCode getStatus)
    {
        var service = new FakeDispatchService();
        using var factory = CreateFactory(service);
        using var client = CreateClient(factory, role);

        using var preview = await client.PostAsJsonAsync(
            "/api/dispatch-plans/preview", PreviewBody());
        using var confirm = await client.PostAsJsonAsync(
            "/api/dispatch-plans", ConfirmBody());
        using var get = await client.GetAsync(
            $"/api/dispatch-plans/{service.Plan.Id}");

        Assert.Equal(previewStatus, preview.StatusCode);
        Assert.Equal(confirmStatus, confirm.StatusCode);
        Assert.Equal(getStatus, get.StatusCode);
    }

    [Fact]
    public async Task Dispatcher_receives_preview_and_confirmed_plan()
    {
        var service = new FakeDispatchService();
        using var factory = CreateFactory(service);
        using var client = CreateClient(factory, "Dispatcher");

        using var previewResponse = await client.PostAsJsonAsync(
            "/api/dispatch-plans/preview", PreviewBody());

        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = await previewResponse.Content
            .ReadFromJsonAsync<DispatchPreviewResponse>();
        Assert.NotNull(preview);
        Assert.Equal(UnassignedId, Assert.Single(preview.UnassignedDeliveryIds));
        Assert.Equal(2, preview.DeliveryVersions.Count);
        Assert.Equal(7.0, Assert.Single(service.LastPreview!.Vehicles).Start.Latitude);
        Assert.Equal("Pickup", preview.Routes[0].Stops[0].Kind);

        using var confirmResponse = await client.PostAsJsonAsync(
            "/api/dispatch-plans", ConfirmBody());

        Assert.Equal(HttpStatusCode.Created, confirmResponse.StatusCode);
        Assert.EndsWith(
            $"/api/dispatch-plans/{service.Plan.Id}",
            confirmResponse.Headers.Location!.ToString());

        var confirmed = await confirmResponse.Content
            .ReadFromJsonAsync<DispatchPlanResponse>();
        Assert.NotNull(confirmed);
        Assert.Equal(service.Plan.Id, confirmed.Id);
        Assert.Equal(
            new[] { DispatchStopKind.Pickup, DispatchStopKind.Dropoff },
            service.LastConfirm!.Routes[0].Stops
                .Select(stop => stop.Kind).ToArray());
        Assert.Equal(
            new[] { 0, 1 },
            confirmed.Routes[0].Stops
                .Select(stop => stop.Position).ToArray());

        using var getResponse = await client.GetAsync(
            confirmResponse.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }

    [Fact]
    public async Task Invalid_stop_kind_is_rejected_and_stale_preview_conflicts()
    {
        var service = new FakeDispatchService();
        using var factory = CreateFactory(service);
        using var client = CreateClient(factory, "Dispatcher");

        var invalid = ConfirmBody() with
        {
            Routes =
            [
                new DispatchReviewedRouteRequest(
                    VehicleId, [new(DeliveryId, "Unknown")])
            ]
        };

        using var badRequest = await client.PostAsJsonAsync(
            "/api/dispatch-plans", invalid);
        Assert.Equal(HttpStatusCode.BadRequest, badRequest.StatusCode);

        service.ConflictOnConfirm = true;
        using var conflict = await client.PostAsJsonAsync(
            "/api/dispatch-plans", ConfirmBody());
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    public async Task Numeric_stop_kind_is_rejected(string kind)
    {
        var service = new FakeDispatchService();
        using var factory = CreateFactory(service);
        using var client = CreateClient(factory, "Dispatcher");
        var request = ConfirmBody() with
        {
            Routes = [new DispatchReviewedRouteRequest(VehicleId,
                [new(DeliveryId, kind), new(DeliveryId, "Dropoff")])]
        };

        using var response = await client.PostAsJsonAsync("/api/dispatch-plans", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(service.LastConfirm);
    }

    [Fact]
    public async Task Oversized_route_is_rejected_before_dispatch_service()
    {
        var service = new FakeDispatchService();
        using var factory = CreateFactory(service);
        using var client = CreateClient(factory, "Dispatcher");
        var request = ConfirmBody() with
        {
            Routes = [new DispatchReviewedRouteRequest(VehicleId,
                Enumerable.Repeat(new DispatchReviewedStopRequest(DeliveryId, "Pickup"), 31)
                    .ToArray())]
        };

        using var response = await client.PostAsJsonAsync("/api/dispatch-plans", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(service.LastConfirm);
    }

    private static PreviewDispatchRequest PreviewBody() => new(
        [new(VehicleId, new CoordinateRequest(7.0, 125.6))],
        [DeliveryId, UnassignedId]);

    private static ConfirmDispatchRequest ConfirmBody() => new(
        [new(VehicleId, new CoordinateRequest(7.0, 125.6))],
        [
            new DispatchReviewedRouteRequest(
                VehicleId,
                [
                    new(DeliveryId, "Pickup"),
                    new(DeliveryId, "Dropoff")
                ])
        ],
        [new(DeliveryId, new byte[] { 1, 2, 3 })]);

    private static DispatchPlan CreatePlan()
    {
        var route = DispatchRoute.Create(
            VehicleId, DriverId, 7.0, 125.6, 1_000, 120,
            [
                DispatchStop.Create(
                    DeliveryId, DispatchStopKind.Pickup, 0, 7.1, 125.6),
                DispatchStop.Create(
                    DeliveryId, DispatchStopKind.Dropoff, 1, 7.2, 125.7)
            ]);

        return DispatchPlan.Create([route], DateTimeOffset.UtcNow);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        FakeDispatchService service)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting(
                "ConnectionStrings:Dalan",
                "Server=localhost;Database=Unused;Integrated Security=True;TrustServerCertificate=True");

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDispatchService>();
                services.AddSingleton<IDispatchService>(service);
                services.RemoveAll<IDispatchPlanReader>();
                services.AddSingleton<IDispatchPlanReader>(service);

                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "Test";
                    options.DefaultChallengeScheme = "Test";
                    options.DefaultForbidScheme = "Test";
                }).AddScheme<AuthenticationSchemeOptions, RoleHeaderHandler>(
                    "Test", _ => { });
            });
        });

    private static HttpClient CreateClient(
        WebApplicationFactory<Program> factory, string? role)
    {
        var client = factory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });

        if (role is not null)
            client.DefaultRequestHeaders.Add("X-Test-Role", role);

        return client;
    }

    private sealed class FakeDispatchService : IDispatchService, IDispatchPlanReader
    {
        public DispatchPlan Plan { get; } = CreatePlan();
        public bool ConflictOnConfirm { get; set; }
        public DispatchPreviewRequest? LastPreview { get; private set; }
        public DispatchConfirmRequest? LastConfirm { get; private set; }

        public Task<DispatchPreviewResult> PreviewAsync(
            DispatchPreviewRequest request,
            CancellationToken cancellationToken = default)
        {
            LastPreview = request;

            return Task.FromResult(new DispatchPreviewResult(
                [
                    new DispatchPreviewRoute(
                        VehicleId, DriverId, new GeoPoint(7.0, 125.6),
                        [
                            new(DeliveryId, DispatchStopKind.Pickup,
                                new GeoPoint(7.1, 125.6)),
                            new(DeliveryId, DispatchStopKind.Dropoff,
                                new GeoPoint(7.2, 125.7))
                        ],
                        1_000, 120)
                ],
                [UnassignedId],
                [
                    new(DeliveryId, new byte[] { 1, 2, 3 }),
                    new(UnassignedId, new byte[] { 4, 5, 6 })
                ], request.Strategy));
        }

        public Task<DispatchPlan?> ConfirmAsync(
            DispatchConfirmRequest request,
            CancellationToken cancellationToken = default)
        {
            LastConfirm = request;
            return Task.FromResult<DispatchPlan?>(
                ConflictOnConfirm ? null : Plan);
        }

        public Task<DispatchPlan?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
            => Task.FromResult<DispatchPlan?>(
                id == Plan.Id ? Plan : null);
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
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role))
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "dispatch-test"),
                    new Claim(ClaimTypes.Role, role.ToString())
                ],
                Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(
                    new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
