using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using Dalan.Contracts.Vehicles;
using Dalan.Contracts.Drivers;
using Dalan.Contracts.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dalan.IntegrationTests;

[Collection("SQL Server")]
public sealed class VehiclesApiTests(SqlServerFixture fixture)
{
    private WebApplicationFactory<Program> CreateFactory()
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Dalan", fixture.ConnectionString);
        });

    private static async Task<HttpClient> CreateAuthorizedClientAsync(
        WebApplicationFactory<Program> factory, string role = "Admin")
    {
        using var scope = factory.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync(role))
            Assert.True((await roles.CreateAsync(new IdentityRole(role))).Succeeded);

        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var email = $"test-{Guid.NewGuid():N}@example.test";
        const string password = "TestPassword-12345";
        var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
        Assert.True((await users.CreateAsync(user, password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);

        var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var login = await client.PostAsJsonAsync("/api/auth/login", new StaffLoginRequest(email, password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var token = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", token.RootElement.GetProperty("accessToken").GetString());
        return client;
    }

    [Fact]
    public async Task Create_returns_location_and_get_returns_persisted_dto()
    {
        using var factory = CreateFactory();
        using var client = await CreateAuthorizedClientAsync(factory);
        var registration = Guid.NewGuid().ToString("N");

        using var response = await client.PostAsJsonAsync("/api/vehicles", new RegisterVehicleRequest(" " + registration + " "));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<VehicleResponse>();
        Assert.NotNull(created);
        Assert.Equal(registration.ToUpperInvariant(), created.RegistrationNumber);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.NotNull(response.Headers.Location);
        Assert.EndsWith($"/api/vehicles/{created.Id}", response.Headers.Location.ToString());
        var retrieved = await client.GetFromJsonAsync<VehicleResponse>(response.Headers.Location);
        Assert.Equal(created, retrieved);

        using var duplicate = await client.PostAsJsonAsync("/api/vehicles", new RegisterVehicleRequest(registration.ToUpperInvariant()));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(409, (await duplicate.Content.ReadFromJsonAsync<ProblemDetails>())!.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ1234567")]
    public async Task Invalid_registration_returns_validation_problem(string? registration)
    {
        using var factory = CreateFactory();
        using var client = await CreateAuthorizedClientAsync(factory);
        using var response = await client.PostAsJsonAsync("/api/vehicles", new RegisterVehicleRequest(registration));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("RegistrationNumber", problem!.Errors.Keys);
    }

    [Fact]
    public async Task Missing_vehicle_returns_not_found()
    {
        using var factory = CreateFactory();
        using var client = await CreateAuthorizedClientAsync(factory);
        using var response = await client.GetAsync($"/api/vehicles/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_staff_can_read_their_profile()
    {
        using var factory = CreateFactory();
        using var client = await CreateAuthorizedClientAsync(factory, "Dispatcher");

        using var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var profile = await response.Content
            .ReadFromJsonAsync<StaffProfileResponse>();

        Assert.NotNull(profile);
        Assert.False(string.IsNullOrWhiteSpace(profile.Email));
        Assert.Contains("Dispatcher", profile.Roles);
    }

    [Fact]
    public async Task Admin_and_dispatcher_can_list_registered_vehicles()
    {
        using var factory = CreateFactory();
        using var admin = await CreateAuthorizedClientAsync(factory);

        var registration = Guid.NewGuid().ToString("N");
        using var create = await admin.PostAsJsonAsync(
            "/api/vehicles",
            new RegisterVehicleRequest(registration));

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        using var adminList = await admin.GetAsync("/api/vehicles");

        Assert.Equal(HttpStatusCode.OK, adminList.StatusCode);

        var adminVehicles = await adminList.Content
            .ReadFromJsonAsync<IReadOnlyList<VehicleResponse>>();

        Assert.Contains(
            adminVehicles!,
            vehicle => vehicle.RegistrationNumber == registration.ToUpperInvariant());

        using var dispatcher = await CreateAuthorizedClientAsync(
            factory,
            "Dispatcher");

        using var response = await dispatcher.GetAsync("/api/vehicles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var vehicles = await response.Content
            .ReadFromJsonAsync<IReadOnlyList<VehicleResponse>>();

        Assert.Contains(
            vehicles!,
            vehicle => vehicle.RegistrationNumber == registration.ToUpperInvariant());
    }

    [Fact]
    public async Task Anonymous_vehicle_requests_are_rejected()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var list = await client.GetAsync("/api/vehicles");
        using var profile = await client.GetAsync("/api/auth/me");

        using var get = await client.GetAsync($"/api/vehicles/{Guid.NewGuid()}");
        using var post = await client.PostAsJsonAsync("/api/vehicles", new RegisterVehicleRequest("TEST-123"));
        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, profile.StatusCode);
    }

    [Fact]
    public async Task Dispatcher_can_read_but_cannot_register_vehicle()
    {
        using var factory = CreateFactory();
        using var client = await CreateAuthorizedClientAsync(factory, "Dispatcher");

        using var get = await client.GetAsync($"/api/vehicles/{Guid.NewGuid()}");
        using var post = await client.PostAsJsonAsync("/api/vehicles", new RegisterVehicleRequest("TEST-123"));
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
    }

    [Fact]
    public async Task Admin_can_update_and_deactivate_vehicle_metadata()
    {
        using var factory = CreateFactory();
        using var admin = await CreateAuthorizedClientAsync(factory);
        var registration = "V-" + Guid.NewGuid().ToString("N")[..12];
        using var create = await admin.PostAsJsonAsync("/api/vehicles",
            new RegisterVehicleRequest(registration, "Van", 500, 3,
                new DateOnly(2027, 3, 1)));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var vehicle = await create.Content.ReadFromJsonAsync<VehicleResponse>();
        Assert.Equal("Van", vehicle!.Type);

        using var invalid = await admin.PutAsJsonAsync($"/api/vehicles/{vehicle.Id}",
            new UpdateVehicleRequest("Van", -1, 3, "Available", null));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var update = await admin.PutAsJsonAsync($"/api/vehicles/{vehicle.Id}",
            new UpdateVehicleRequest("Box truck", 1200, 2, "Maintenance",
                new DateOnly(2027, 4, 1)));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var changed = await update.Content.ReadFromJsonAsync<VehicleResponse>();
        Assert.Equal("Box truck", changed!.Type);
        Assert.Equal("Maintenance", changed.Availability);

        using var deactivate = await admin.PostAsync($"/api/vehicles/{vehicle.Id}/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        var inactive = await deactivate.Content.ReadFromJsonAsync<VehicleResponse>();
        Assert.Equal("Inactive", inactive!.Availability);
    }

    [Fact]
    public async Task Dispatcher_cannot_update_or_deactivate_vehicles()
    {
        using var factory = CreateFactory();
        using var dispatcher = await CreateAuthorizedClientAsync(factory, "Dispatcher");
        var id = Guid.NewGuid();
        using var update = await dispatcher.PutAsJsonAsync($"/api/vehicles/{id}",
            new UpdateVehicleRequest(null, null, null, "Available", null));
        using var deactivate = await dispatcher.PostAsync($"/api/vehicles/{id}/deactivate", null);
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deactivate.StatusCode);
    }

    [Fact]
    public async Task Admin_driver_api_enforces_unique_assignments_and_staff_roles()
    {
        using var factory = CreateFactory();
        using var admin = await CreateAuthorizedClientAsync(factory);
        using var dispatcher = await CreateAuthorizedClientAsync(factory, "Dispatcher");
        using var anonymous = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var forbidden = await dispatcher.GetAsync("/api/drivers");
        using var unauthorized = await anonymous.GetAsync("/api/drivers");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        using var vehicleCreate = await admin.PostAsJsonAsync("/api/vehicles",
            new RegisterVehicleRequest("D-" + Guid.NewGuid().ToString("N")[..12]));
        var vehicle = await vehicleCreate.Content.ReadFromJsonAsync<VehicleResponse>();
        Assert.NotNull(vehicle);

        var license = "L-" + Guid.NewGuid().ToString("N")[..12];
        using var create = await admin.PostAsJsonAsync("/api/drivers",
            new RegisterDriverRequest("Ada Driver", "09170000000", license,
                new DateOnly(2028, 1, 1)));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var driver = await create.Content.ReadFromJsonAsync<DriverResponse>();
        Assert.NotNull(driver);
        Assert.EndsWith($"/api/drivers/{driver.Id}", create.Headers.Location!.ToString());

        using var assign = await admin.PutAsJsonAsync($"/api/drivers/{driver.Id}",
            new UpdateDriverRequest("Ada Driver", "09170000000", license,
                new DateOnly(2028, 1, 1), "Available", vehicle.Id));
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);
        Assert.Equal(vehicle.Id, (await assign.Content.ReadFromJsonAsync<DriverResponse>())!.AssignedVehicleId);

        using var secondCreate = await admin.PostAsJsonAsync("/api/drivers",
            new RegisterDriverRequest("Bob Driver", "09170000001",
                "L-" + Guid.NewGuid().ToString("N")[..12], new DateOnly(2028, 1, 1)));
        var second = await secondCreate.Content.ReadFromJsonAsync<DriverResponse>();
        Assert.NotNull(second);
        using var conflict = await admin.PutAsJsonAsync($"/api/drivers/{second.Id}",
            new UpdateDriverRequest(second.Name, second.Phone, second.LicenseNumber,
                second.LicenseExpiry, "Available", vehicle.Id));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        using var deactivate = await admin.PostAsync($"/api/drivers/{driver.Id}/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        var inactive = await deactivate.Content.ReadFromJsonAsync<DriverResponse>();
        Assert.Equal("Inactive", inactive!.Availability);
        Assert.Null(inactive.AssignedVehicleId);
    }
}
