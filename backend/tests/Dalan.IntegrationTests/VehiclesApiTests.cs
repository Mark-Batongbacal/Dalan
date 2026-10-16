using System.Net;
using System.Net.Http.Json;
using Dalan.Contracts.Vehicles;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
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

    [Fact]
    public async Task Create_returns_location_and_get_returns_persisted_dto()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
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
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var response = await client.PostAsJsonAsync("/api/vehicles", new RegisterVehicleRequest(registration));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("RegistrationNumber", problem!.Errors.Keys);
    }

    [Fact]
    public async Task Missing_vehicle_returns_not_found()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync($"/api/vehicles/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
