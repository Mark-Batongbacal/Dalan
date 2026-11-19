using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Dalan.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dalan.IntegrationTests;

[Collection("SQL Server")]
public sealed class StaffWorkspaceTests(SqlServerFixture fixture)
{
    [Theory]
    [InlineData("Admin")]
    [InlineData("Dispatcher")]
    public async Task Staff_navigation_authorization_and_invalid_forms_preserve_values(string role)
    {
        using var factory = new WebApplicationFactory<WebAssemblyMarker>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Testing").UseSetting("ConnectionStrings:Dalan", fixture.ConnectionString));
        using var client = factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        var email = Guid.NewGuid().ToString("N") + "@dalan.test";
        var password = "Fixture!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)) + "a7";
        Guid vehicleId;
        Guid driverId;
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            if (!await roles.RoleExistsAsync(role)) Assert.True((await roles.CreateAsync(new IdentityRole(role))).Succeeded);
            var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            Assert.True((await users.CreateAsync(user, password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
            var vehicles = scope.ServiceProvider.GetRequiredService<Dalan.Application.Vehicles.IVehicleService>();
            var vehicle = await vehicles.RegisterAsync(new("QA-" + Guid.NewGuid().ToString("N")[..8], "Delivery van", 750, 4));
            vehicleId = vehicle.Vehicle!.Id;
            var drivers = scope.ServiceProvider.GetRequiredService<Dalan.Application.Drivers.IDriverService>();
            var driver = await drivers.RegisterAsync(new("Workspace driver", "09123456789", Guid.NewGuid().ToString("N"), DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1)));
            driverId = driver.Driver!.Id;
        }
        var login = await client.GetStringAsync("/Account/Login");
        using var accepted = await Post("/Account/Login", login, new() { ["Email"] = email, ["Password"] = password });
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        var fleet = await client.GetStringAsync("/Vehicles");
        Assert.Contains("aria-controls=\"main-navigation\"", fleet);
        Assert.Equal(role == "Admin", fleet.Contains("href=\"/StaffAccounts/Create\""));
        Assert.Equal(role == "Dispatcher", fleet.Contains("href=\"/Deliveries\""));
        foreach (var route in new[] { "/Vehicles/Create", "/Drivers", "/Drivers/Create", "/StaffAccounts/Create" })
        {
            using var response = await client.GetAsync(route);
            Assert.Equal(role == "Admin" ? HttpStatusCode.OK : HttpStatusCode.Redirect, response.StatusCode);
            if (role == "Dispatcher") Assert.Contains("/Account/Denied", response.Headers.Location!.ToString());
        }
        if (role == "Admin")
        {
            foreach (var item in new[] {
                (Route: "/Vehicles/Create", Field: "RegistrationNumber", Value: "STAFF-FORM-CHECK", Invalid: "PayloadCapacityKg"),
                (Route: "/Vehicles/Edit/" + vehicleId, Field: "Type", Value: "Preserved van", Invalid: "PayloadCapacityKg"),
                (Route: "/Drivers/Edit/" + driverId, Field: "Name", Value: "Preserved driver", Invalid: "Phone"),
                (Route: "/Drivers/Create", Field: "Name", Value: "Staff form check", Invalid: "Phone"),
                (Route: "/StaffAccounts/Create", Field: "Email", Value: "new-staff@example.test", Invalid: "Password") })
            {
                var html = await client.GetStringAsync(item.Route);
                using var invalid = await Post(item.Route, html, new() { ["Id"] = item.Route.Contains("/Vehicles/") ? vehicleId.ToString() : driverId.ToString(), [item.Field] = item.Value, [item.Invalid] = item.Invalid == "PayloadCapacityKg" ? "-1" : "" });
                var result = await invalid.Content.ReadAsStringAsync();
                Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
                Assert.Contains("validation-summary-errors", result);
                Assert.Contains("value=\"" + item.Value + "\"", result);
                Assert.Contains("aria-describedby=\"" + item.Invalid + "-validation\"", result);
                Assert.Contains("id=\"" + item.Invalid + "-validation\"", result);
            }
        }
        if (Environment.GetEnvironmentVariable("DALAN_STAFF_BROWSER") == "1")
        {
            var info = new System.Diagnostics.ProcessStartInfo("node") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "../../../../../tests/browser/staff-workspace.e2e.cjs"));
            using var browser = System.Diagnostics.Process.Start(info)!;
            await browser.StandardInput.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new { email, password, role, vehicleId, driverId, connection = fixture.ConnectionString }));
            browser.StandardInput.Close();
            var output = browser.StandardOutput.ReadToEndAsync();
            var error = browser.StandardError.ReadToEndAsync();
            await browser.WaitForExitAsync();
            Assert.True(browser.ExitCode == 0, await output + await error);
        }

        Task<HttpResponseMessage> Post(string route, string html, Dictionary<string, string> values)
        {
            values["__RequestVerificationToken"] = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            return client.PostAsync(route, new FormUrlEncodedContent(values));
        }
    }
}
