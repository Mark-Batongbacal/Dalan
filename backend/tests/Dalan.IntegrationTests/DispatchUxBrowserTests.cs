using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Dalan.Domain.Deliveries;
using Dalan.Domain.Drivers;
using Dalan.Domain.Vehicles;
using Dalan.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dalan.IntegrationTests;

[Collection("SQL Server")]
public sealed class DispatchUxBrowserTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Dispatcher_cookie_workflow_retains_choices_and_saves_confirmed_plan()
    {
        if (Environment.GetEnvironmentVariable("DALAN_DISPATCH_BROWSER") != "1") return;
        using var factory = new WebApplicationFactory<WebAssemblyMarker>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Testing").UseSetting("ConnectionStrings:Dalan", fixture.ConnectionString));
        var suffix = Guid.NewGuid().ToString("N");
        var email = suffix + "@dalan.test";
        var password = "Fixture!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)) + "a7";
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            if (!await roles.RoleExistsAsync("Dispatcher")) Assert.True((await roles.CreateAsync(new("Dispatcher"))).Succeeded);
            var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            Assert.True((await users.CreateAsync(user, password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, "Dispatcher")).Succeeded);
        }
        var vehicle = Vehicle.Register("UX-" + suffix[..10], "Van", 100);
        var driver = Driver.Register("UX Driver", "09123456789", suffix, DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1));
        driver.AssignVehicle(vehicle.Id);
        var delivery = Delivery.Create("UX Recipient", "Warehouse", 14.6, 121, "Destination", 14.61, 121.01, DateTimeOffset.UtcNow, 5);
        await using (var db = fixture.CreateContext())
        {
            // Keep an unselected vehicle before the selected one to exercise reindexed review fields.
            db.AddRange(Vehicle.Register("AA-" + suffix[..10], "Unused van", 100), vehicle, driver, delivery);
            await db.SaveChangesAsync();
        }
        var info = new ProcessStartInfo("node") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "../../../../../tests/browser/dispatch-ux.e2e.cjs"));
        using var browser = Process.Start(info)!;
        await browser.StandardInput.WriteAsync(JsonSerializer.Serialize(new { email, password, vehicleId = vehicle.Id, deliveryId = delivery.Id, connection = fixture.ConnectionString }));
        browser.StandardInput.Close();
        var output = browser.StandardOutput.ReadToEndAsync();
        var errors = browser.StandardError.ReadToEndAsync();
        await browser.WaitForExitAsync();
        Assert.True(browser.ExitCode == 0, await output + await errors);
        await using var verify = fixture.CreateContext();
        var saved = await verify.DispatchPlans.Include(x => x.Routes).ThenInclude(x => x.Stops)
            .SingleAsync(x => x.Routes.Any(r => r.Stops.Any(s => s.DeliveryId == delivery.Id)));
        Assert.Equal(2, Assert.Single(saved.Routes).Stops.Count);
        Assert.Equal("WorkloadEquity", saved.Strategy);
        Assert.Equal(DeliveryStatus.Assigned, (await verify.Deliveries.FindAsync(delivery.Id))!.Status);
    }
}
