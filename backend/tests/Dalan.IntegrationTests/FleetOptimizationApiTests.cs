using Dalan.Application.FleetOptimization;
using Dalan.Application.Vehicles;
using Dalan.Contracts.FleetOptimization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Xunit;

namespace Dalan.IntegrationTests;

public sealed class FleetOptimizationApiTests
{
    private static readonly Guid VehicleId = Guid.NewGuid();

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("Admin", HttpStatusCode.Forbidden)]
    [InlineData("Dispatcher", HttpStatusCode.OK)]
    public async Task Optimization_requires_dispatcher_and_returns_ordered_plan(
        string? role, HttpStatusCode expected)
    {
        using var factory = CreateFactory(new FakeOptimizer());
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        if (role is not null) client.DefaultRequestHeaders.Add("X-Test-Role", role);

        using var response = await client.PostAsJsonAsync("/api/fleet/optimize", new FleetOptimizeRequest(
            [new(VehicleId, new CoordinateRequest(14.6, 121.0))],
            [new("JOB-1", new(14.61, 121.01), new(14.62, 121.02))]));

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            var plan = await response.Content.ReadFromJsonAsync<FleetOptimizeResponse>();
            Assert.Equal("JOB-1", plan!.Routes[0].Stops[0].JobId);
            Assert.Equal("Pickup", plan.Routes[0].Stops[0].Kind);
        }
    }

    [Fact]
    public async Task Optimization_returns_service_unavailable_when_routing_is_not_configured()
    {
        using var factory = CreateFactory(optimizer: null);
        using var client = factory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Test-Role", "Dispatcher");

        using var response = await client.PostAsJsonAsync(
            "/api/fleet/optimize",
            new FleetOptimizeRequest(
                [new(VehicleId, new CoordinateRequest(14.6, 121.0))],
                [new("JOB-1", new(14.61, 121.01), new(14.62, 121.02))]));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Routing is unavailable", problem!.Title);
    }

    [Fact]
    public async Task Optimization_rejects_an_unregistered_vehicle()
    {
        using var factory = CreateFactory(new FakeOptimizer());
        using var client = factory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Test-Role", "Dispatcher");

        using var response = await client.PostAsJsonAsync(
            "/api/fleet/optimize",
            new FleetOptimizeRequest(
                [new(Guid.NewGuid(), new CoordinateRequest(14.6, 121.0))],
                [new("JOB-1", new(14.61, 121.01), new(14.62, 121.02))]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("Vehicles", problem!.Errors.Keys);
    }

    [Fact]
    public async Task Optimization_returns_bad_gateway_when_routing_provider_fails()
    {
        using var factory = CreateFactory(new ThrowingOptimizer(new HttpRequestException()));
        using var client = factory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Test-Role", "Dispatcher");

        using var response = await client.PostAsJsonAsync(
            "/api/fleet/optimize",
            new FleetOptimizeRequest(
                [new(VehicleId, new CoordinateRequest(14.6, 121.0))],
                [new("JOB-1", new(14.61, 121.01), new(14.62, 121.02))]));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Routing provider failed", problem!.Title);
    }

    [Fact]
    public async Task Optimization_returns_gateway_timeout_when_routing_provider_times_out()
    {
        using var factory = CreateFactory(new ThrowingOptimizer(new TaskCanceledException()));
        using var client = factory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Test-Role", "Dispatcher");

        using var response = await client.PostAsJsonAsync(
            "/api/fleet/optimize",
            new FleetOptimizeRequest(
                [new(VehicleId, new CoordinateRequest(14.6, 121.0))],
                [new("JOB-1", new(14.61, 121.01), new(14.62, 121.02))]));

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Routing provider timed out", problem!.Title);
    }

    [Fact]
    public async Task Optimization_returns_bad_gateway_when_routing_provider_returns_invalid_data()
    {
        using var factory = CreateFactory(new ThrowingOptimizer(new InvalidDataException()));
        using var client = factory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Test-Role", "Dispatcher");

        using var response = await client.PostAsJsonAsync(
            "/api/fleet/optimize",
            new FleetOptimizeRequest(
                [new(VehicleId, new CoordinateRequest(14.6, 121.0))],
                [new("JOB-1", new(14.61, 121.01), new(14.62, 121.02))]));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Routing provider returned invalid data", problem!.Title);
    }

    private static WebApplicationFactory<Program> CreateFactory(IFleetOptimizer? optimizer)
    => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting(
            "ConnectionStrings:Dalan",
            "Server=localhost;Database=Unused;Integrated Security=True;TrustServerCertificate=True");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IVehicleService>();
            services.AddScoped<IVehicleService, FakeVehicleService>();

            services.RemoveAll<IFleetOptimizer>();
            if (optimizer is not null)
                services.AddScoped<IFleetOptimizer>(_ => optimizer);

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "Test";
                options.DefaultChallengeScheme = "Test";
                options.DefaultForbidScheme = "Test";
            }).AddScheme<AuthenticationSchemeOptions, RoleHeaderHandler>("Test", _ => { });
        });
    });

    private sealed class FakeVehicleService : IVehicleService
    {
        public Task<VehicleDetails?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<VehicleDetails?>(id == VehicleId ? new(id, "TEST-001") : null);
        public Task<IReadOnlyList<VehicleDetails>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<VehicleDetails>>([new VehicleDetails(VehicleId, "TEST-001")]);
        public Task<VehicleRegistrationResult> RegisterAsync(
            RegisterVehicleCommand command, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<VehicleMutationResult> UpdateAsync(
            Guid id, UpdateVehicleCommand command, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<VehicleMutationResult> DeactivateAsync(
            Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeOptimizer : IFleetOptimizer
    {
        public Task<FleetOptimizationResult> OptimizeAsync(
            FleetOptimizationRequest request, CancellationToken cancellationToken = default)
        {
            var job = request.Jobs[0];
            return Task.FromResult(new FleetOptimizationResult(
                [new OptimizedVehicleRoute(request.Vehicles[0].Id,
                    [new FleetStop(job.Id, FleetStopKind.Pickup, job.Pickup),
                     new FleetStop(job.Id, FleetStopKind.Dropoff, job.Dropoff)], 1200, 300)],
                []));
        }
    }

    private sealed class ThrowingOptimizer(Exception error) : IFleetOptimizer
    {
        public Task<FleetOptimizationResult> OptimizeAsync(
            FleetOptimizationRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromException<FleetOptimizationResult>(error);
    }

    private sealed class RoleHeaderHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role))
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "test"),
                 new Claim(ClaimTypes.Role, role.ToString())], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
