using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Dalan.Application.Deliveries;
using Dalan.Application.Dispatch;
using Dalan.Application.Routing;
using Dalan.Contracts.Dispatch;
using Dalan.Domain.Dispatch;
using Dalan.Web.Controllers;
using Dalan.Web.Models;
using Dalan.Application.Drivers;
using Dalan.Application.Vehicles;
using Dalan.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dalan.IntegrationTests;

public sealed class DispatchBoardWebTests
{
    [Fact]
    public async Task Opening_selector_does_not_calculate_a_preview()
    {
        var service = new RecordingDispatchService();
        using var factory = CreateFactory(service);
        using var client = CreateClient(factory, "Dispatcher");
        using var response = await client.GetAsync("/DispatchBoard");

        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Optimization strategy", html);
        Assert.Contains("default policy", html);
        Assert.Null(service.LastPreview);
    }

    [Theory]
    [InlineData(DispatchStrategy.MinimumTotalTravelTime)]
    [InlineData(DispatchStrategy.WorkloadEquity)]
    [InlineData(DispatchStrategy.WarehouseClusterRotation)]
    [InlineData(DispatchStrategy.EarliestFleetCompletion)]
    public async Task Preview_review_and_confirm_carry_strategy_and_review_recomputes(DispatchStrategy strategy)
    {
        var service = new RecordingDispatchService();
        var controller = CreateController(service);
        var board = new DispatchBoardViewModel
        {
            Strategy = strategy,
            Settings = new() { ServiceSecondsPerStop = 75, WarehouseLatitude = 14.6, WarehouseLongitude = 121 },
            Vehicles = [new() { Id = service.VehicleId, Selected = true, StartLatitude = 14.6, StartLongitude = 121 }],
            Deliveries = [new() { Id = service.DeliveryId, Selected = true }]
        };

        await controller.Index(board, CancellationToken.None);
        Assert.NotNull(board.Preview);
        Assert.Equal(board.Strategy, service.LastPreview!.Strategy);
        var result = Assert.IsType<ViewResult>(await controller.Review(board, CancellationToken.None));
        var review = Assert.IsType<DispatchReviewViewModel>(result.Model);
        Assert.Equal(2, service.PreviewCalls);
        Assert.Equal(board.Strategy, review.Strategy);
        Assert.Equal(75, review.Settings.ServiceSecondsPerStop);
        Assert.Equal(17, review.RotationVersion);
        await controller.Confirm(review, CancellationToken.None);
        Assert.Equal(review.Strategy, service.LastConfirm!.Strategy);
        Assert.Equal(75, service.LastConfirm.Settings!.ServiceSecondsPerStop);
        Assert.Equal(17, service.LastConfirm.RotationVersion);
    }

    [Fact]
    public async Task Invalid_bound_strategy_cannot_fall_back_to_default()
    {
        var service = new RecordingDispatchService();
        var controller = CreateController(service);
        controller.ModelState.AddModelError("Strategy", "Invalid value");
        var board = new DispatchBoardViewModel();
        await controller.Index(board, CancellationToken.None);
        await controller.Review(board, CancellationToken.None);
        await controller.Confirm(new DispatchReviewViewModel(), CancellationToken.None);
        Assert.Null(service.LastPreview);
        Assert.Null(service.LastConfirm);
    }

    [Fact]
    public async Task Metadata_suggestion_does_not_execute_preview_or_change_selection()
    {
        var service = new RecordingDispatchService();
        var controller = CreateController(service);
        var board = new DispatchBoardViewModel
        {
            Strategy = DispatchStrategy.WorkloadEquity,
            Vehicles = [new() { Id = service.VehicleId, Selected = true }],
            Deliveries = [new() { Id = service.DeliveryId, Selected = true }]
        };
        var view = Assert.IsType<ViewResult>(await controller.SuggestStrategy(board, CancellationToken.None));
        Assert.Equal("Index", view.ViewName);
        Assert.NotNull(board.Advice);
        Assert.Equal(DispatchStrategy.WorkloadEquity, board.Strategy);
        Assert.Null(service.LastPreview);
        Assert.Null(service.LastConfirm);
    }

    private sealed class RecordingDispatchService : IDispatchService
    {
        public Guid VehicleId { get; } = Guid.NewGuid();
        public Guid DeliveryId { get; } = Guid.NewGuid();
        public DispatchPreviewRequest? LastPreview { get; private set; }
        public DispatchConfirmRequest? LastConfirm { get; private set; }
        public int PreviewCalls { get; private set; }
        public Task<DispatchPreviewResult> PreviewAsync(DispatchPreviewRequest request, CancellationToken cancellationToken = default)
        {
            LastPreview = request;
            PreviewCalls++;
            return Task.FromResult(new DispatchPreviewResult(
                [new(VehicleId, Guid.NewGuid(), new GeoPoint(14.6, 121),
                    [new(DeliveryId, DispatchStopKind.Pickup, new GeoPoint(14.6, 121)),
                     new(DeliveryId, DispatchStopKind.Dropoff, new GeoPoint(14.7, 121))], 1000, 120)],
                [], [new(DeliveryId, [1, 2, 3])], request.Strategy, RotationVersion: 17));
        }
        public Task<DispatchPlan?> ConfirmAsync(DispatchConfirmRequest request, CancellationToken cancellationToken = default)
        {
            LastConfirm = request;
            return Task.FromResult<DispatchPlan?>(null);
        }
    }

    [Theory]
    [InlineData("/Deliveries/Create", null, HttpStatusCode.Unauthorized)]
    [InlineData("/Deliveries/Create", "Admin", HttpStatusCode.Forbidden)]
    [InlineData("/Deliveries/Create", "Dispatcher", HttpStatusCode.OK)]
    [InlineData("/DispatchBoard", null, HttpStatusCode.Unauthorized)]
    [InlineData("/DispatchBoard", "Admin", HttpStatusCode.Forbidden)]
    [InlineData("/DispatchBoard", "Dispatcher", HttpStatusCode.OK)]
    [InlineData("/DispatchBoard/Drivers", null, HttpStatusCode.Unauthorized)]
    [InlineData("/DispatchBoard/Drivers", "Admin", HttpStatusCode.Forbidden)]
    [InlineData("/DispatchBoard/Drivers", "Dispatcher", HttpStatusCode.OK)]
    public async Task Pages_require_dispatcher(
        string path, string? role, HttpStatusCode expected)
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory, role);

        using var response = await client.GetAsync(path);

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("Admin", HttpStatusCode.Forbidden)]
    public async Task Create_post_rejects_non_dispatchers(
        string? role, HttpStatusCode expected)
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory, role);

        using var response = await client.PostAsync(
            "/Deliveries/Create",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["RecipientName"] = "Test recipient",
                ["PickupAddress"] = "Pickup",
                ["PickupLatitude"] = "14.6",
                ["PickupLongitude"] = "121.0",
                ["DropoffAddress"] = "Drop-off",
                ["DropoffLatitude"] = "14.7",
                ["DropoffLongitude"] = "121.1"
            }));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Confirm_rejects_missing_version_token_without_throwing()
    {
        var vehicleId = Guid.NewGuid();
        var deliveryId = Guid.NewGuid();
        var model = new DispatchReviewViewModel
        {
            Vehicles =
            [
                new()
                {
                    VehicleId = vehicleId,
                    StartLatitude = 14.6,
                    StartLongitude = 121.0
                }
            ],
            Stops =
            [
                new() { DeliveryId = deliveryId, VehicleId = vehicleId, Kind = "Pickup", Order = 0 },
                new() { DeliveryId = deliveryId, VehicleId = vehicleId, Kind = "Dropoff", Order = 1 }
            ],
            Versions =
            [
                new() { DeliveryId = deliveryId, RowVersion = null! }
            ]
        };

        var controller = CreateController();
        var result = await controller.Confirm(model, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Review", view.ViewName);
        Assert.Contains("incomplete", model.Error!);
    }

    [Fact]
    public async Task Confirm_rejects_null_form_entries_without_throwing()
    {
        var model = new DispatchReviewViewModel
        {
            Vehicles = [null!],
            Stops = [null!, null!],
            Versions = [null!]
        };

        var controller = CreateController();
        var result = await controller.Confirm(model, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Review", view.ViewName);
        Assert.Empty(model.Vehicles);
        Assert.Empty(model.Stops);
        Assert.Empty(model.Versions);
    }

    private static DispatchBoardController CreateController(RecordingDispatchService? dispatch = null)
        => new(
            new FakeDeliveryService { Items = dispatch is null ? [] :
                [new(dispatch.DeliveryId, "Recipient", "Warehouse", 14.6, 121, "Dropoff", 14.7, 121,
                    null, null, null, null, Dalan.Domain.Deliveries.DeliveryStatus.Pending,
                    DateTimeOffset.UtcNow, null, null, null, null, null)] },
            new FakeDriverService(),
            new FakeVehicleService { Items = dispatch is null ? [] : [new(dispatch.VehicleId, "TEST")] },
            new FakeDispatchPlanReader(),
            dispatch is null ? new ServiceCollection().BuildServiceProvider()
                : new ServiceCollection().AddSingleton<IDispatchService>(dispatch).BuildServiceProvider());

    private sealed class FakeDispatchPlanReader : IDispatchPlanReader
    {
        public Task<DispatchPlan?> GetByIdAsync(
            Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<DispatchPlan?>(null);
    }

    private static WebApplicationFactory<WebAssemblyMarker> CreateFactory(IDispatchService? dispatch = null)
        => new WebApplicationFactory<WebAssemblyMarker>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting(
                    "ConnectionStrings:Dalan",
                    "Server=localhost;Database=Unused;Integrated Security=True;TrustServerCertificate=True");

                builder.ConfigureTestServices(services =>
                {
                    if (dispatch is not null)
                    {
                        services.RemoveAll<IDispatchService>();
                        services.AddSingleton(dispatch);
                    }
                    services.RemoveAll<IDeliveryService>();
                    services.RemoveAll<IVehicleService>();
                    services.RemoveAll<IDriverService>();
                    services.AddScoped<IDeliveryService, FakeDeliveryService>();
                    services.AddScoped<IVehicleService, FakeVehicleService>();
                    services.AddScoped<IDriverService, FakeDriverService>();

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
        WebApplicationFactory<WebAssemblyMarker> factory, string? role)
    {
        var client = factory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        if (role is not null)
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private sealed class FakeDeliveryService : IDeliveryService
    {
        public IReadOnlyList<DeliveryDetails> Items { get; init; } = [];
        public Task<IReadOnlyList<DeliveryDetails>> ListAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(Items);

        public Task<DeliveryDetails?> GetByIdAsync(
            Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<DeliveryDetails?>(null);

        public Task<DeliveryMutationResult> CreateAsync(
            CreateDeliveryCommand command,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<DeliveryMutationResult> AssignAsync(
            Guid id, AssignDeliveryCommand command,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<DeliveryMutationResult> MarkPickedUpAsync(
            Guid id, Guid driverId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<DeliveryMutationResult> CompleteAsync(
            Guid id, CompleteDeliveryCommand command,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<DeliveryMutationResult> CancelAsync(
            Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeDriverService : IDriverService
    {
        public Task<IReadOnlyList<DriverDetails>> ListAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DriverDetails>>([]);

        public Task<DriverDetails?> GetByIdAsync(
            Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<DriverDetails?>(null);

        public Task<DriverMutationResult> RegisterAsync(
            RegisterDriverCommand command,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<DriverMutationResult> UpdateAsync(
            Guid id, UpdateDriverCommand command,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<DriverMutationResult> DeactivateAsync(
            Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeVehicleService : IVehicleService
    {
        public IReadOnlyList<VehicleDetails> Items { get; init; } = [];
        public Task<IReadOnlyList<VehicleDetails>> ListAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(Items);

        public Task<VehicleDetails?> GetByIdAsync(
            Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<VehicleDetails?>(null);

        public Task<VehicleRegistrationResult> RegisterAsync(
            RegisterVehicleCommand command,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<VehicleMutationResult> UpdateAsync(
            Guid id, UpdateVehicleCommand command,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<VehicleMutationResult> DeactivateAsync(
            Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
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
                    new Claim(ClaimTypes.NameIdentifier, "test"),
                    new Claim(ClaimTypes.Role, role.ToString())
                ],
                Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(
                    new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
