using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Dalan.Application.Routing;
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

public sealed class PlanningMapWebTests
{
    [Theory]
    [InlineData("2040-01-01T12:30:00Z", "2040-01-01T13:00:00Z")]
    [InlineData("2040-01-01T23:50:00Z", "2040-01-02T00:00:00Z")]
    public async Task Create_defaults_to_a_future_half_hour_and_renders_warehouse_and_local_time_controls(string now, string expected)
    {
        using var factory = Factory().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(new FrozenTime(DateTimeOffset.Parse(now)))));
        using var client = Client(factory);
        var html = await client.GetStringAsync("/Deliveries/Create");
        var hidden = Regex.Matches(html, "<input[^>]*>").Cast<Match>()
            .Single(match => match.Value.Contains("id=\"ExpectedDeliveryAtUtc\"", StringComparison.Ordinal)).Value;
        var value = Regex.Match(hidden, "value=\"([^\"]+)\"").Groups[1].Value;
        Assert.Equal(DateTimeOffset.Parse(expected), DateTimeOffset.Parse(WebUtility.HtmlDecode(value)));
        Assert.Contains("type=\"datetime-local\"", html);
        Assert.Contains("step=\"1800\"", html);
        Assert.Contains("Warehouse address or name", html);
        Assert.Contains("data-warehouse-selection", html);
    }

    private sealed class FrozenTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("Admin", HttpStatusCode.Forbidden)]
    [InlineData("Driver", HttpStatusCode.Forbidden)]
    public async Task Provider_endpoints_require_dispatcher(string? role, HttpStatusCode expected)
    {
        using var factory = Factory();
        using var client = Client(factory, role);
        foreach (var endpoint in new[] { "Route", "Search" })
        {
            using var response = await client.PostAsJsonAsync("/PlanningMap/" + endpoint, new { });
            Assert.Equal(expected, response.StatusCode);
        }
    }

    [Fact]
    public async Task Provider_endpoints_require_antiforgery()
    {
        using var factory = Factory();
        using var client = Client(factory);
        using var response = await client.PostAsJsonAsync("/PlanningMap/Route", new { points = new[] { new GeoPoint(0, 0), new(1, 1) } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_page_renders_safe_model_and_search_does_not_route()
    {
        var routing = new FakeRouting();
        using var factory = Factory(routing, new FakeSearch());
        using var client = Client(factory);
        var html = await Token(client);
        Assert.Contains("data-planning-map", html);
        Assert.Contains("OpenStreetMap contributors", html);
        Assert.Contains("maplibre-gl.js", html);
        using var response = await client.PostAsJsonAsync("/PlanningMap/Search", new { query = "Manila" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("Photon", json);
        Assert.Equal(0, routing.Calls);
    }

    [Fact]
    public async Task Road_geometry_keeps_order_skips_duplicate_stops_and_does_not_dispatch()
    {
        var routing = new FakeRouting();
        using var factory = Factory(routing);
        using var client = Client(factory);
        await Token(client);
        using var response = await client.PostAsJsonAsync("/PlanningMap/Route",
            new { points = new[] { new GeoPoint(0, 0), new(0, 0), new(1, 1), new(2, 2) } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, routing.Calls);
        Assert.Equal(new GeoPoint(1, 1), routing.LastOrigin);
        Assert.Equal(new GeoPoint(2, 2), routing.LastDestination);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(32)]
    public async Task Road_request_bounds_are_checked_before_provider_calls(int count)
    {
        var routing = new FakeRouting();
        using var factory = Factory(routing);
        using var client = Client(factory);
        await Token(client);
        using var response = await client.PostAsJsonAsync("/PlanningMap/Route",
            new { points = Enumerable.Repeat(new GeoPoint(0, 0), count) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, routing.Calls);
    }

    [Fact]
    public async Task Invalid_coordinates_and_short_search_are_rejected()
    {
        var routing = new FakeRouting();
        using var factory = Factory(routing);
        using var client = Client(factory);
        await Token(client);
        using var response = await client.PostAsJsonAsync("/PlanningMap/Route",
            new { points = new[] { new GeoPoint(91, 0), new(0, 0) } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var search = await client.PostAsJsonAsync("/PlanningMap/Search", new { query = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, search.StatusCode);
        Assert.Equal(0, routing.Calls);
    }

    [Theory]
    [InlineData("missing", HttpStatusCode.ServiceUnavailable)]
    [InlineData("unreachable", HttpStatusCode.UnprocessableEntity)]
    [InlineData("http", HttpStatusCode.BadGateway)]
    [InlineData("timeout", HttpStatusCode.GatewayTimeout)]
    [InlineData("cancel", HttpStatusCode.GatewayTimeout)]
    public async Task Road_failure_states_are_explicit(string mode, HttpStatusCode expected)
    {
        using var factory = Factory(mode == "missing" ? null : new FakeRouting { Mode = mode });
        using var client = Client(factory);
        await Token(client);
        using var response = await client.PostAsJsonAsync("/PlanningMap/Route",
            new { points = new[] { new GeoPoint(0, 0), new(1, 1) } });
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Missing_geocoder_keeps_manual_selection_available()
    {
        using var factory = Factory();
        using var client = Client(factory);
        await Token(client);
        using var response = await client.PostAsJsonAsync("/PlanningMap/Search", new { query = "Manila" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("Select a map point", await response.Content.ReadAsStringAsync());
    }

    private static async Task<string> Token(HttpClient client)
    {
        var html = await client.GetStringAsync("/Deliveries/Create");
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        client.DefaultRequestHeaders.Remove("RequestVerificationToken");
        client.DefaultRequestHeaders.Add("RequestVerificationToken", WebUtility.HtmlDecode(token));
        return html;
    }

    private static WebApplicationFactory<WebAssemblyMarker> Factory(IRoutingService? routing = null, IPlaceSearchService? search = null)
        => new WebApplicationFactory<WebAssemblyMarker>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Dalan", "Server=localhost;Database=Unused;Integrated Security=True;TrustServerCertificate=True");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRoutingService>(); services.RemoveAll<IPlaceSearchService>();
                if (routing is not null) services.AddSingleton(routing);
                if (search is not null) services.AddSingleton(search);
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "MapTest";
                    options.DefaultChallengeScheme = "MapTest";
                    options.DefaultForbidScheme = "MapTest";
                }).AddScheme<AuthenticationSchemeOptions, RoleHandler>("MapTest", _ => { });
            });
        });

    private static HttpClient Client(WebApplicationFactory<WebAssemblyMarker> factory, string? role = "Dispatcher")
    {
        var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        if (role is not null) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private sealed class RoleHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "test"), new Claim(ClaimTypes.Role, role.ToString())], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class FakeRouting : IRoutingService
    {
        public string Mode { get; init; } = "";
        public int Calls { get; private set; }
        public GeoPoint? LastOrigin { get; private set; }
        public GeoPoint? LastDestination { get; private set; }
        public Task<RoadRoute?> GetRouteAsync(GeoPoint origin, GeoPoint destination, CancellationToken token)
        {
            Calls++; LastOrigin = origin; LastDestination = destination;
            if (Mode == "http") throw new HttpRequestException();
            if (Mode == "timeout") throw new TaskCanceledException();
            if (Mode == "cancel") throw new OperationCanceledException();
            return Task.FromResult<RoadRoute?>(Mode == "unreachable" ? null : new(1000, 120, "test-geometry"));
        }
    }
    private sealed class FakeSearch : IPlaceSearchService
    {
        public Task<IReadOnlyList<PlaceSearchResult>> SearchAsync(string query, CancellationToken token)
            => Task.FromResult<IReadOnlyList<PlaceSearchResult>>([new("Manila", new(14.6, 121.0))]);
    }
}
