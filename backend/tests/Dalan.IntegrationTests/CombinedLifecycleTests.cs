using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dalan.Domain.Drivers;
using Dalan.Infrastructure;
using Dalan.Infrastructure.Notifications;
using Dalan.Infrastructure.Persistence;
using Dalan.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
namespace Dalan.IntegrationTests;
[Collection("SQL Server")]
public sealed class CombinedLifecycleTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Invitation_outbox_activation_recovery_revocation_and_audit_work_together()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var keys = Path.Combine(Path.GetTempPath(), "DalanLifecycleVerificationKeys_" + suffix);
        var handler = new MailTransport();
        try
        {
            using var web = new WebApplicationFactory<WebAssemblyMarker>().WithWebHostBuilder(b => b.UseEnvironment("Testing")
             .UseSetting("ConnectionStrings:Dalan", fixture.ConnectionString)
             .UseSetting("DriverInvitations:Enabled", "true").UseSetting("DriverInvitations:PublicBaseUrl", "https://dalan.test")
             .UseSetting("DriverRecovery:Enabled", "true").UseSetting("DriverRecovery:PublicBaseUrl", "https://dalan.test")
             .UseSetting("AccountAudit:Enabled", "true").UseSetting("DriverCredentialEmail:Enabled", "true")
             .UseSetting("DriverCredentialEmail:KeyRingPath", keys).UseSetting("Resend:Enabled", "true")
             .UseSetting("Resend:ApiKey", "fake").UseSetting("Resend:From", "test@dalan.test").UseSetting("Resend:Recipients", "test@dalan.test")
             .ConfigureTestServices(s => Transport(s, handler)));
            var driver = Driver.Register("Combined fixture Driver", "09123456789", suffix, DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1));
            var adminEmail = "admin-" + suffix + "@dalan.test"; var email = "driver-" + suffix + "@dalan.test";
            const string adminPassword = "Combined-Admin!2026"; const string password = "Combined-Driver!2026";
            using (var scope = web.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<DalanDbContext>(); db.Drivers.Add(driver); await db.SaveChangesAsync();
                var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                if (!await roles.RoleExistsAsync("Admin")) Assert.True((await roles.CreateAsync(new("Admin"))).Succeeded);
                var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
                var adminUser = new IdentityUser { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true };
                Assert.True((await users.CreateAsync(adminUser, adminPassword)).Succeeded); Assert.True((await users.AddToRoleAsync(adminUser, "Admin")).Succeeded);
            }
            using var admin = web.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
            var login = await admin.GetStringAsync("/Account/Login");
            var af = Csrf(login);
            Assert.Equal(HttpStatusCode.Redirect, (await admin.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = adminEmail, ["Password"] = adminPassword, ["__RequestVerificationToken"] = af }))).StatusCode);
            var form = await admin.GetStringAsync("/Drivers/" + driver.Id + "/Access");
            admin.DefaultRequestHeaders.Add("RequestVerificationToken", Csrf(form));
            handler.Fail = true;
            Assert.Equal(HttpStatusCode.Accepted, (await admin.PostAsJsonAsync("/api/driver-invitations/issue", new { driverId = driver.Id, email })).StatusCode);
            using (var retryScope = web.Services.CreateScope())
                await retryScope.ServiceProvider.GetRequiredService<IDriverCredentialEmailOutbox>().RetryPendingAsync(default);
            var invitationText = Assert.Single(handler.Texts); var invitationToken = Token(invitationText);
            await using (var db = fixture.CreateContext())
            {
                var row = await db.NotificationDeliveries.SingleAsync(x => x.Kind == "driver-invite" && x.Id.Contains("driver-invite-"));
                Assert.Null(row.AcceptedAtUtc); Assert.DoesNotContain(invitationToken, row.Text);
                await db.NotificationDeliveries.Where(x => x.Id == row.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.NextAttemptAtUtc, (DateTimeOffset?)null));
            }
            // A fresh provider/service container uses the shared key ring like the separate Worker.
            handler.Fail = false;
            var config = web.Services.GetRequiredService<IConfiguration>();
            var services = new ServiceCollection().AddLogging().AddSingleton(TimeProvider.System).AddSingleton(config);
            services.AddDalanInfrastructure(config); Transport(services, handler);
            await using (var worker = services.BuildServiceProvider())
            {
                await using var scope = worker.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<IDriverCredentialEmailOutbox>().RetryPendingAsync(default);
            }
            Assert.Equal(2, handler.Texts.Count); Assert.Equal(handler.Texts[0], handler.Texts[1]);
            if (Environment.GetEnvironmentVariable("DALAN_STAFF_BROWSER") == "1")
                await CredentialBrowser(new { connection = fixture.ConnectionString, keys, email, token = invitationToken, password, activate = true });
            else
                Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/driver-invitations/activate", new { email, token = invitationToken, password })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/driver-invitations/activate", new { email, token = invitationToken, password })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/DriverInvitations/Activate")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/DriverRecovery/Reset")).StatusCode);
            using var api = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing").UseSetting("ConnectionStrings:Dalan", fixture.ConnectionString).UseSetting("AccountAudit:Enabled", "true"));
            using var mobile = api.CreateClient(new() { BaseAddress = new("https://localhost") });
            async Task Login(string pass)
            {
                using var response = await mobile.PostAsJsonAsync("/api/driver/login", new { email, password = pass }); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                mobile.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
            }
            await Login(password);
            Assert.Equal(HttpStatusCode.OK, (await mobile.GetAsync("/api/driver/session")).StatusCode);
            Assert.Equal(HttpStatusCode.Accepted, (await admin.PostAsJsonAsync("/api/driver-recovery/request", new { email })).StatusCode);
            using (var retryScope = web.Services.CreateScope())
                await retryScope.ServiceProvider.GetRequiredService<IDriverCredentialEmailOutbox>().RetryPendingAsync(default);
            var resetToken = Token(handler.Texts.Last()); const string newPassword = "Combined-New-Password!2026";
            if (Environment.GetEnvironmentVariable("DALAN_STAFF_BROWSER") == "1")
                await CredentialBrowser(new { connection = fixture.ConnectionString, keys, email, token = resetToken, password = newPassword, activate = false });
            else
                Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/driver-recovery/reset", new { email, token = resetToken, password = newPassword })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await mobile.GetAsync("/api/driver/session")).StatusCode); await Login(newPassword);
            foreach (var action in new[] { "disable", "enable" })
            {
                Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/admin/driver-mobile-access/{driver.Id}/{action}", new { reason = "Controlled local lifecycle verification" })).StatusCode);
                Assert.Equal(HttpStatusCode.Unauthorized, (await mobile.GetAsync("/api/driver/session")).StatusCode);
            }
            await Login(newPassword);
            var audit = await admin.GetFromJsonAsync<JsonElement>("/api/admin/account-audit?driverId=" + driver.Id);
            var actions = audit.EnumerateArray().Select(x => x.GetProperty("action").GetString()).ToArray();
            foreach (var expected in new[] { "DriverInvitationIssued", "DriverInvitationActivated", "DriverRecoveryRequested", "DriverPasswordReset", "DriverMobileDisabled", "DriverMobileEnabled", "DriverLogin" }) Assert.Contains(expected, actions);
            var serialized = audit.ToString(); Assert.DoesNotContain(password, serialized); Assert.DoesNotContain(resetToken, serialized); Assert.DoesNotContain(email, serialized);
            await using (var db = fixture.CreateContext())
            {
                var rows = await db.NotificationDeliveries.Where(x => x.Kind == "driver-invite" || x.Kind == "driver-recovery").ToListAsync();
                Assert.Equal(2, rows.Count); Assert.All(rows, r => { Assert.NotNull(r.AcceptedAtUtc); Assert.Empty(r.Text); });
            }
            if (Environment.GetEnvironmentVariable("DALAN_ALL_UX_BROWSER") == "1")
            {
                var inviteDriver = Driver.Register("Browser invite fixture", "09123456789", Guid.NewGuid().ToString("N"), DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1));
                var dispatcherEmail = "dispatcher-" + suffix + "@dalan.test";
                using (var scope = web.Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<DalanDbContext>(); db.Drivers.Add(inviteDriver); await db.SaveChangesAsync();
                    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                    if (!await roles.RoleExistsAsync("Dispatcher")) Assert.True((await roles.CreateAsync(new("Dispatcher"))).Succeeded);
                    var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
                    var dispatcher = new IdentityUser { UserName = dispatcherEmail, Email = dispatcherEmail, EmailConfirmed = true };
                    Assert.True((await users.CreateAsync(dispatcher, adminPassword)).Succeeded); Assert.True((await users.AddToRoleAsync(dispatcher, "Dispatcher")).Succeeded);
                }
                var gpsDriver = Driver.Register("GPS browser fixture", "09123456789", Guid.NewGuid().ToString("N"), DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1));
                var vehicle = Dalan.Domain.Vehicles.Vehicle.Register("GPSUI-" + suffix[..8]); gpsDriver.AssignVehicle(vehicle.Id);
                var assigned = DateTimeOffset.UtcNow.AddMinutes(-4);
                var delivery = Dalan.Domain.Deliveries.Delivery.Create("GPS fixture recipient", "Warehouse", 14, 121, "Destination", 14.1, 121.1, assigned.AddMinutes(-1));
                delivery.Assign(gpsDriver.Id, vehicle.Id, assigned); delivery.MarkPickedUp(gpsDriver.Id, assigned.AddMinutes(1));
                var proof = new Dalan.Contracts.DriverExecution.DriverTaskEvent(Guid.NewGuid(), assigned, assigned.AddMinutes(2), "Deliver", null, null, "Fixture recipient", [255, 216, 255, 224, 255, 217]);
                await using (var db = fixture.CreateContext())
                {
                    db.AddRange(gpsDriver, vehicle, delivery, new DriverGpsException
                    {
                        Id = proof.EventId,
                        DriverId = gpsDriver.Id,
                        DeliveryId = delivery.Id,
                        AssignedAtUtc = assigned,
                        SubmittedAtUtc = DateTimeOffset.UtcNow,
                        Reason = "Fixture GPS unavailable at building",
                        Payload = JsonSerializer.Serialize(proof),
                        PayloadHash = new string('A', 64)
                    });
                    await db.SaveChangesAsync();
                }
                await CredentialBrowser(new { connection = fixture.ConnectionString, keys, adminEmail, adminPassword, dispatcherEmail, inviteDriverId = inviteDriver.Id, inviteEmail = "invite-" + suffix + "@dalan.test", gpsReviewId = proof.EventId, vehicleId = vehicle.Id, deliveryId = delivery.Id }, "lifecycle-accessibility.e2e.cjs");
                await using (var db = fixture.CreateContext())
                {
                    Assert.Equal("Approved", (await db.DriverGpsExceptions.SingleAsync(x => x.Id == proof.EventId)).Status);
                    Assert.Equal(Dalan.Domain.Deliveries.DeliveryStatus.Delivered, (await db.Deliveries.FindAsync(delivery.Id))!.Status);
                }
            }

        }
        finally { if (Directory.Exists(keys) && Path.GetFullPath(keys).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) Directory.Delete(keys, true); }
    }
    static async Task CredentialBrowser(object fixture, string script = "driver-credential.e2e.cjs")
    {
        var info = new ProcessStartInfo("node") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "../../../../../tests/browser/", script));
        using var process = Process.Start(info)!;
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(fixture)); process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(); Assert.True(process.ExitCode == 0, await output + await error);
    }
    static string Csrf(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    static string Token(string text) => Uri.UnescapeDataString(new Uri(text.Split('\n').Single(x => x.StartsWith("https://"))).Fragment.TrimStart('#').Split('&').Single(x => x.StartsWith("token="))[6..]);
    static void Transport(IServiceCollection s, MailTransport handler) { s.RemoveAll<ResendEmailSender>(); s.AddSingleton(new ResendEmailSender(new HttpClient(handler, false) { BaseAddress = new Uri("https://resend.example.invalid/") }, new() { Enabled = true, ApiKey = "fake" })); }
    sealed class MailTransport : HttpMessageHandler { public bool Fail; public List<string> Texts = new(); protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) { var json = JsonDocument.Parse(await r.Content!.ReadAsStringAsync(ct)); Texts.Add(json.RootElement.GetProperty("text").GetString()!); return new(Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK) { Content = JsonContent.Create(new { id = "fake-accepted" }) }; } }
}
