using System.Net.Http.Json;
using System.Text.Json;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Dalan.Application.Routing;
using Dalan.Application.Tracking;
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

public sealed class ProviderAndFleetWebTests
{
    [Theory]
    [InlineData("Admin")]
    [InlineData("Dispatcher")]
    public async Task Staff_receive_real_metrics_and_embedded_address_results_without_coordinates(string role)
    {
        var repository = new Repository(); var provider = new Provider();
        using var factory = Factory(repository, provider);
        using var client = Client(factory, role);
        using var fleet = await client.GetAsync("/FleetTracking");
        var html = await fleet.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, fleet.StatusCode); Assert.True(fleet.Headers.CacheControl!.NoStore);
        Assert.Contains("data-observed-utilization>40.0%", html);
        Assert.Contains("longer gaps are excluded", html);
        Assert.DoesNotContain("google-maps-logo", html);
        var reads = repository.ActivityCalls;
        using var positions = await client.GetAsync("/FleetTracking/Positions");
        Assert.Equal(HttpStatusCode.OK, positions.StatusCode); Assert.True(positions.Headers.CacheControl!.NoStore);
        Assert.Equal(reads, repository.ActivityCalls); // Polling does not scan historical samples.
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Places")).StatusCode);
        var token = Token(html);
        client.DefaultRequestHeaders.Add("RequestVerificationToken", token);
        using var results = await client.PostAsJsonAsync("/PlanningMap/Addresses", new { query = "123 Manila Street" });
        Assert.Equal(HttpStatusCode.OK, results.StatusCode); Assert.True(results.Headers.CacheControl!.NoStore);
        var json = await results.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("123 Manila Street", json.GetProperty("results")[0].GetProperty("address").GetString());
        Assert.False(json.GetProperty("results")[0].TryGetProperty("location", out _));
        Assert.Equal(1, provider.Calls);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Notifications")).StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("Driver", HttpStatusCode.Forbidden)]
    public async Task Non_staff_cannot_read_tracking_places_or_email(string? role, HttpStatusCode expected)
    {
        var repository = new Repository(); var provider = new Provider();
        using var factory = Factory(repository, provider); using var client = Client(factory, role);
        foreach (var path in new[] { "/FleetTracking", "/FleetTracking/Positions", "/Places", "/Notifications" })
            Assert.Equal(expected, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(0, repository.ActivityCalls); Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Places_requires_antiforgery_and_valid_query_and_reports_missing_configuration()
    {
        var provider = new Provider(); using var factory = Factory(new(), provider); using var client = Client(factory, "Admin");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Places", new FormUrlEncodedContent(new Dictionary<string, string> { ["Query"] = "Manila" }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/PlanningMap/Addresses", new { query = "Manila" })).StatusCode);
        client.DefaultRequestHeaders.Add("RequestVerificationToken", Token(await client.GetStringAsync("/FleetTracking")));
        var invalid = await client.PostAsJsonAsync("/PlanningMap/Addresses", new { query = "a" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode); Assert.Equal(0, provider.Calls);
        using var missing = Factory(new(), null); using var absentClient = Client(missing, "Dispatcher");
        absentClient.DefaultRequestHeaders.Add("RequestVerificationToken", Token(await absentClient.GetStringAsync("/FleetTracking")));
        var unavailable = await absentClient.PostAsJsonAsync("/PlanningMap/Addresses", new { query = "Manila" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.Contains("not configured", await unavailable.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Provider_errors_and_invalid_period_do_not_render_success()
    {
        var repository = new Repository(); using var factory = Factory(repository, new() { Fail = true }); using var client = Client(factory, "Admin");
        client.DefaultRequestHeaders.Add("RequestVerificationToken", Token(await client.GetStringAsync("/FleetTracking")));
        var failure = await client.PostAsJsonAsync("/PlanningMap/Addresses", new { query = "Manila" });
        Assert.Equal(HttpStatusCode.BadGateway, failure.StatusCode);
        Assert.DoesNotContain("results", await failure.Content.ReadAsStringAsync());
        var invalid = await client.GetStringAsync("/FleetTracking?from=2000-01-01&to=2000-01-02");
        Assert.DoesNotContain("data-observed-utilization", invalid); Assert.Equal(1, repository.ActivityCalls);
    }

    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private static WebApplicationFactory<WebAssemblyMarker> Factory(Repository repository, Provider? provider)
        => new WebApplicationFactory<WebAssemblyMarker>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Testing").UseSetting("ConnectionStrings:Dalan", "Server=localhost;Database=Unused;Integrated Security=True;TrustServerCertificate=True")
            .UseSetting("Tracking:Enabled", "true").UseSetting("Resend:Enabled", "false")
            .ConfigureTestServices(services =>
            {
                services.RemoveAll<IFleetTrackingRepository>(); services.AddSingleton<IFleetTrackingRepository>(repository);
                services.RemoveAll<IGoogleAddressSearch>(); if (provider is not null) services.AddSingleton<IGoogleAddressSearch>(provider);
                services.AddAuthentication(options => { options.DefaultAuthenticateScheme = "ProviderTest"; options.DefaultChallengeScheme = "ProviderTest"; options.DefaultForbidScheme = "ProviderTest"; })
                    .AddScheme<AuthenticationSchemeOptions, RoleHandler>("ProviderTest", _ => { });
            }));
    private static HttpClient Client(WebApplicationFactory<WebAssemblyMarker> factory, string? role)
    {
        var client = factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        if (role is not null) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }
    private sealed class Provider : IGoogleAddressSearch
    {
        public int Calls; public bool Fail;
        public Task<IReadOnlyList<StreetAddressSuggestion>> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            Calls++; if (Fail) throw new HttpRequestException();
            return Task.FromResult<IReadOnlyList<StreetAddressSuggestion>>([new("123 Manila Street")]);
        }
    }
    private sealed class Repository : IFleetTrackingRepository
    {
        public int ActivityCalls;
        public Task<TrackingStart?> StartAsync(Guid driverId, DateTimeOffset now, CancellationToken token) => throw new NotSupportedException();
        public Task<TrackingWriteStatus> RecordAsync(Guid driverId, Guid sessionId, TrackingFix fix, DateTimeOffset now, CancellationToken token) => throw new NotSupportedException();
        public Task StopAsync(Guid driverId, Guid sessionId, DateTimeOffset now, CancellationToken token) => throw new NotSupportedException();
        public Task CleanupAsync(DateTimeOffset now, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<VehiclePosition>> PositionsAsync(DateTimeOffset now, CancellationToken token) => Task.FromResult<IReadOnlyList<VehiclePosition>>([]);
        public Task<ObservedFleetActivity> ActivityAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken token)
        { ActivityCalls++; return Task.FromResult(new ObservedFleetActivity(150, 60, 1000, 5)); }
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
}
