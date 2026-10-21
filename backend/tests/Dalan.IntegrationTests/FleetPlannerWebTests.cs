using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Dalan.Application.Vehicles;
using Dalan.Web;
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

public sealed class FleetPlannerWebTests
{
    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("Admin", HttpStatusCode.Forbidden)]
    [InlineData("Dispatcher", HttpStatusCode.OK)]
    public async Task Planner_page_requires_dispatcher_and_renders_registered_vehicle(
        string? role, HttpStatusCode expected)
    {
        using var factory = new WebApplicationFactory<WebAssemblyMarker>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Dalan", "Server=localhost;Database=Unused;Integrated Security=True;TrustServerCertificate=True");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IVehicleService>();
                services.AddScoped<IVehicleService, FakeVehicleService>();
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "Test";
                    options.DefaultChallengeScheme = "Test";
                    options.DefaultForbidScheme = "Test";
                }).AddScheme<AuthenticationSchemeOptions, RoleHeaderHandler>("Test", _ => { });
            });
        });
        using var client = factory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        if (role is not null) client.DefaultRequestHeaders.Add("X-Test-Role", role);

        using var response = await client.GetAsync("/FleetPlanner");

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            var page = await response.Content.ReadAsStringAsync();
            Assert.Contains("Fleet Planner", page);
            Assert.Contains("TEST-001", page);
        }
    }

    private sealed class FakeVehicleService : IVehicleService
    {
        public Task<VehicleDetails?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<VehicleDetails?>(null);
        public Task<IReadOnlyList<VehicleDetails>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<VehicleDetails>>(
                [new VehicleDetails(Guid.NewGuid(), "TEST-001")]);
        public Task<VehicleRegistrationResult> RegisterAsync(
            RegisterVehicleCommand command, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
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
