using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Dalan.Application.Analytics;
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

public sealed class AnalyticsWebTests
{
    [Theory]
    [InlineData("Admin")]
    public async Task Overview_uses_real_reporting_period_and_registry_availability(string role)
    {
        var repository = new Repository { Snapshot = new(new(10,2,2,1,4,1,2,0,0,null),new(7,3,2,1,2,4000,900),[],[]) };
        using var factory = Factory(repository);
        using var client = Client(factory, role);
        using var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Delivery progress", html);
        Assert.Contains("40%", html);
        Assert.Contains("Vehicles currently marked available", html);
        Assert.Contains("Last 30 days", html);
        Assert.Equal(new DateOnly(2026,10,19), repository.Period!.From);
        Assert.Equal(1, repository.Calls);
        Assert.True(response.Headers.CacheControl!.NoStore);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Driver")]
    public async Task Overview_does_not_query_or_expose_metrics_to_non_staff(string? role)
    {
        var repository = new Repository();
        using var factory = Factory(repository);
        using var client = Client(factory, role);
        var html = await client.GetStringAsync("/");
        Assert.DoesNotContain("dashboard-report", html);
        Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task Dispatcher_home_enters_workspace_without_querying_overview()
    {
        var repository = new Repository();
        using var factory = Factory(repository);
        using var client = Client(factory, "Dispatcher");
        using var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/DispatchBoard", response.Headers.Location?.ToString());
        Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task Overview_provider_failure_keeps_workspace_links_without_fake_metrics()
    {
        var repository = new Repository { Fail = true };
        using var factory = Factory(repository);
        using var client = Client(factory,"Admin");
        var html = await client.GetStringAsync("/");
        Assert.Contains("overview is temporarily unavailable", html);
        Assert.Contains("View vehicles", html);
        Assert.DoesNotContain("dashboard-report", html);
    }
    [Theory]
    [InlineData("Admin")]
    [InlineData("Dispatcher")]
    public async Task Staff_can_read_default_report_and_navigation(string role)
    {
        var repository = new Repository();
        using var factory = Factory(repository);
        using var client = Client(factory, role);
        using var response = await client.GetAsync("/Analytics");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-analytics-report", html);
        Assert.Contains("No deliveries were created", html);
        Assert.Contains("No vehicles are registered", html);
        Assert.Contains("How to read this report", html);
        Assert.Contains("not GPS-measured utilization", html);
        Assert.Contains("data-analytics-loading", html);
        Assert.Contains("href=\"/Analytics\"", html);
        Assert.Equal(new DateOnly(2026, 10, 19), repository.Period!.From);
        Assert.Equal(new DateOnly(2026, 11, 17), repository.Period.To);
        Assert.True(response.Headers.CacheControl!.NoStore);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("Driver", HttpStatusCode.Forbidden)]
    public async Task Non_staff_are_denied_before_repository_access(string? role, HttpStatusCode expected)
    {
        var repository = new Repository();
        using var factory = Factory(repository);
        using var client = Client(factory, role);
        using var response = await client.GetAsync("/Analytics");
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(0, repository.Calls);
    }

    [Theory]
    [InlineData("from=bad&to=2026-11-17")]
    [InlineData("from=2026-11-18&to=2026-11-17")]
    [InlineData("from=2025-01-01&to=2026-11-17")]
    [InlineData("from=2026-11-17")]
    [InlineData("from=9999-12-30&to=9999-12-31")]
    public async Task Invalid_period_does_not_query_or_render_fake_metrics(string query)
    {
        var repository = new Repository();
        using var factory = Factory(repository);
        using var client = Client(factory, "Admin");
        var html = await client.GetStringAsync("/Analytics?" + query);
        Assert.Contains("validation-summary-errors", html);
        Assert.Contains("Check the reporting period", html);
        Assert.DoesNotContain("data-analytics-report", html);
        Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task Filter_uses_selected_period_and_ignores_posted_report_fields()
    {
        var repository = new Repository();
        using var factory = Factory(repository);
        using var client = Client(factory, "Dispatcher");
        using var response = await client.GetAsync("/Analytics?from=2026-11-01&to=2026-11-02&Report.Deliveries.Total=999&Error=fake");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, repository.Period!.Days);
        Assert.DoesNotContain("999 deliveries created", html);
        Assert.DoesNotContain(">fake<", html);
    }

    [Fact]
    public async Task Unavailable_data_returns_503_without_a_zero_dashboard()
    {
        using var factory = Factory(new Repository { Fail = true });
        using var client = Client(factory, "Admin");
        using var response = await client.GetAsync("/Analytics");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Analytics is temporarily unavailable", html);
        Assert.Contains("Report unavailable", html);
        Assert.Contains("unavailable data is not a zero result", html);
        Assert.DoesNotContain("data-analytics-report", html);
    }

    [Fact]
    public async Task Long_period_exposes_exact_daily_alternative_and_full_fleet_denominator()
    {
        var rows = Enumerable.Range(0, 50).Select(i => new VehicleActivity(Guid.NewGuid(), $"VEH-{i:D3}", "Available", 1, 1500, 120)).ToArray();
        var repository = new Repository { Snapshot = new(new(8, 2, 1, 1, 3, 1, 1, 1, 2, 10800),
            new(51, 51, 1, 2, 2, 3500, 300), [new(new(2026, 11, 17), 8)], rows) };
        using var factory = Factory(repository);
        using var client = Client(factory, "Dispatcher");
        var html = await client.GetStringAsync("/Analytics?from=2025-11-17&to=2026-11-17");
        Assert.Contains("366 inclusive calendar days", html);
        Assert.Contains("366 dates; peak 8", html);
        Assert.Contains("Showing 50 of 51 current vehicles", html);
        Assert.Contains("Fleet totals include all vehicles", html);
        Assert.Contains("Exact delivery creation counts for every selected UTC date", html);
        Assert.Contains("Current statuses", html);
        Assert.Contains("not historical statuses", html);
        Assert.Contains("saved planning estimates", html);
        Assert.Contains("2025-11-17</th>", html);
        Assert.Contains("2026-11-17</th>", html);
    }

    private static WebApplicationFactory<WebAssemblyMarker> Factory(Repository repository)
        => new WebApplicationFactory<WebAssemblyMarker>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Dalan", "Server=localhost;Database=Unused;Integrated Security=True;TrustServerCertificate=True");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAnalyticsRepository>(); services.AddSingleton<IAnalyticsRepository>(repository);
                services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(new Clock());
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "AnalyticsTest"; options.DefaultChallengeScheme = "AnalyticsTest";
                    options.DefaultForbidScheme = "AnalyticsTest";
                }).AddScheme<AuthenticationSchemeOptions, RoleHandler>("AnalyticsTest", _ => { });
            });
        });

    private static HttpClient Client(WebApplicationFactory<WebAssemblyMarker> factory, string? role)
    {
        var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        if (role is not null) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }
    private sealed class Clock : TimeProvider
    { public override DateTimeOffset GetUtcNow() => new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero); }
    private sealed class Repository : IAnalyticsRepository
    {
        public int Calls { get; private set; }
        public bool Fail { get; init; }
        public AnalyticsSnapshot Snapshot { get; init; } = new(new(0, 0, 0, 0, 0, 0, 0, 0, 0, null), new(0, 0, 0, 0, 0, 0, 0), [], []);
        public AnalyticsPeriod? Period { get; private set; }
        public Task<AnalyticsSnapshot> ReadAsync(AnalyticsPeriod period, DateTimeOffset asOfUtc, CancellationToken token)
        {
            Calls++; Period = period;
            if (Fail) throw new AnalyticsUnavailableException("Unavailable", new Exception());
            return Task.FromResult(Snapshot);
        }
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
