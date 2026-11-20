using System.Net;
using System.Text.RegularExpressions;
using Dalan.Application.Dispatch;
using Dalan.Contracts.Dispatch;
using Dalan.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Dalan.IntegrationTests;

public sealed class WorkflowNavigationWebTests
{
    public static WebApplicationFactory<WebAssemblyMarker> Factory() =>
        DispatchBoardWebTests.CreateFactory().WithWebHostBuilder(builder => builder
            .UseSetting("Tracking:Enabled", "false")
            .ConfigureTestServices(services => {
                services.RemoveAll<IWarehousePreferenceService>();
                services.AddSingleton<IWarehousePreferenceService, Warehouse>();
            }));

    static HttpClient Client(WebApplicationFactory<WebAssemblyMarker> factory, string? role = "Dispatcher") {
        var client = factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        if (role is not null) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    [Theory]
    [InlineData("/Warehouse")]
    [InlineData("/DispatchBoard/Drivers")]
    [InlineData("/Vehicles")]
    [InlineData("/Deliveries/Create")]
    [InlineData("/DeliveryBatch")]
    [InlineData("/DeliveryWorkflow/Assignments")]
    [InlineData("/DispatchBoard")]
    [InlineData("/FleetTracking")]
    [InlineData("/Deliveries")]
    public async Task Workflow_back_link_requires_explicit_guide_origin(string path) {
        using var factory = Factory(); using var client = Client(factory);
        var direct = await client.GetStringAsync(path);
        Assert.DoesNotContain("Back to workflow", direct);
        var guided = await client.GetStringAsync(path + "?workflow=guide");
        Assert.Contains("Back to workflow guide", guided);
        Assert.Contains("data-workflow-origin", guided);
        Assert.DoesNotContain("Choose valid UTC dates", guided);
        Assert.DoesNotContain("workflow=guide", Regex.Match(guided, "<aside.*?</aside>", RegexOptions.Singleline).Value);
    }

    [Fact] public async Task Guide_marks_every_departure_and_profile_owns_sign_out() {
        using var factory = Factory(); using var client = Client(factory);
        var guide = await client.GetStringAsync("/DeliveryWorkflow");
        foreach (var path in new[] { "/Warehouse", "/Vehicles", "/DispatchBoard/Drivers", "/Deliveries/Create", "/DeliveryBatch", "/DeliveryWorkflow/Assignments", "/DispatchBoard", "/FleetTracking", "/Deliveries" })
            Assert.Contains("href=\"" + path + "?workflow=guide\"", guide);
        Assert.DoesNotContain("action=\"/Account/Logout\"", guide);
        Assert.Contains("__RequestVerificationToken", guide);
        Assert.Contains("/Account/Profile", guide);
        Assert.Contains("rel=\"icon\"", guide);
        Assert.Contains("/images/dalan-logo.png", guide);
        var profile = await client.GetStringAsync("/Account/Profile");
        Assert.Contains("action=\"/Account/Logout\"", profile);
        Assert.Contains("__RequestVerificationToken", profile);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Account/Logout", new FormUrlEncodedContent([]))).StatusCode);
    }

    [Fact] public async Task Profile_requires_authentication_and_admin_gets_no_workflow_return() {
        using var factory = Factory(); using var anonymous = Client(factory, null); using var admin = Client(factory, "Admin");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/Account/Profile")).StatusCode);
        Assert.DoesNotContain("Back to workflow guide", await admin.GetStringAsync("/Vehicles?workflow=guide"));
    }

    [Fact] public async Task Guided_form_redirect_keeps_origin_but_direct_form_does_not() {
        using var factory = Factory(); using var client = Client(factory);
        foreach (var query in new[] { "", "?workflow=guide" }) {
            var page = await client.GetStringAsync("/Warehouse" + query);
            var token = WebUtility.HtmlDecode(Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            var response = await client.PostAsync("/Warehouse" + query, new FormUrlEncodedContent(new Dictionary<string,string> {
                ["Name"] = "Test warehouse", ["Latitude"] = "14.6", ["Longitude"] = "121", ["ReturnToDispatch"] = "true", ["__RequestVerificationToken"] = token
            }));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/DispatchBoard" + query, response.Headers.Location?.ToString());
        }
    }

    sealed class Warehouse : IWarehousePreferenceService {
        public Task<WarehousePreference?> GetAsync(string userId, CancellationToken token = default) => Task.FromResult<WarehousePreference?>(null);
        public Task SaveAsync(string userId, SaveWarehousePreference request, CancellationToken token = default) => Task.CompletedTask;
    }
}
