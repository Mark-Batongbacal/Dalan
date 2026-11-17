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
public sealed class StaffLoginSqlTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Login_distinguishes_lockout_and_valid_credentials_issue_a_working_cookie()
    {
        using var factory = new WebApplicationFactory<WebAssemblyMarker>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Testing").UseSetting("ConnectionStrings:Dalan", fixture.ConnectionString));
        using var client = factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        var email = Guid.NewGuid().ToString("N") + "@dalan.test";
        var password = "Fixture!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)) + "a7";
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            if (!await roles.RoleExistsAsync("Admin")) Assert.True((await roles.CreateAsync(new IdentityRole("Admin"))).Succeeded);
            var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            Assert.True((await users.CreateAsync(user, password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, "Admin")).Succeeded);
        }
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var rejected = await Submit("incorrect-fixture-only");
            Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
            var html = await rejected.Content.ReadAsStringAsync();
            Assert.Contains(attempt < 5 ? "Invalid sign-in attempt." : "Sign-in is temporarily locked", html);
        }
        using (var stillLocked = await Submit(password))
            Assert.Contains("Sign-in is temporarily locked", await stillLocked.Content.ReadAsStringAsync());
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = await users.FindByEmailAsync(email);
            Assert.True((await users.SetLockoutEndDateAsync(user!, null)).Succeeded);
            Assert.True((await users.ResetAccessFailedCountAsync(user!)).Succeeded);
        }
        using var accepted = await Submit(password);
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        Assert.Equal("/Analytics", accepted.Headers.Location!.OriginalString);
        using var report = await client.GetAsync("/Analytics");
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        Assert.Contains("data-analytics-report", await report.Content.ReadAsStringAsync());

        async Task<HttpResponseMessage> Submit(string input)
        {
            using var form = await client.GetAsync("/Account/Login?ReturnUrl=%2FAnalytics");
            var html = await form.Content.ReadAsStringAsync();
            Assert.Contains("id=\"toggle-password\"", html);
            var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
            Assert.True(token.Success);
            return await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = email, ["Password"] = input, ["ReturnUrl"] = "/Analytics",
                ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value)
            }));
        }
    }
}
