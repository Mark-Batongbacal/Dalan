using Dalan.Application.Vehicles;
using Dalan.Domain.Vehicles;
using Dalan.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Dalan.IntegrationTests;

[Collection("SQL Server")]
public sealed class VehicleRepositoryTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Registration_is_persisted_and_can_be_read_in_a_new_context()
    {
        var vehicle = Vehicle.Register(Guid.NewGuid().ToString("N"));
        await using (var context = fixture.CreateContext())
        {
            Assert.Equal(AddVehicleOutcome.Added, await new VehicleRepository(context).AddAsync(vehicle));
        }

        await using var readContext = fixture.CreateContext();
        var saved = await new VehicleRepository(readContext).GetByIdAsync(vehicle.Id);
        Assert.Equal(vehicle.RegistrationNumber, saved!.RegistrationNumber);
        Assert.Empty(readContext.ChangeTracker.Entries());
        Assert.Null(await new VehicleRepository(readContext).GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Unique_index_arbitrates_concurrent_registration_and_failed_insert_is_detached()
    {
        var registration = Guid.NewGuid().ToString("N");
        await using var firstContext = fixture.CreateContext();
        await using var secondContext = fixture.CreateContext();
        var outcomes = await Task.WhenAll(
            new VehicleRepository(firstContext).AddAsync(Vehicle.Register(registration.ToLowerInvariant())),
            new VehicleRepository(secondContext).AddAsync(Vehicle.Register(" " + registration + " ")));

        Assert.Single(outcomes, outcome => outcome == AddVehicleOutcome.Added);
        Assert.Single(outcomes, outcome => outcome == AddVehicleOutcome.DuplicateRegistration);
        var failedContext = outcomes[0] == AddVehicleOutcome.DuplicateRegistration ? firstContext : secondContext;
        Assert.Empty(failedContext.ChangeTracker.Entries());
        Assert.Equal(AddVehicleOutcome.Added,
            await new VehicleRepository(failedContext).AddAsync(Vehicle.Register(Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public async Task Migration_has_no_pending_model_changes_and_database_enforces_required_max_length()
    {
        await using var context = fixture.CreateContext();
        Assert.False(context.Database.HasPendingModelChanges());

        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO Vehicles (Id, RegistrationNumber) VALUES ({Guid.NewGuid()}, {new string('X', 33)})"));
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            context.Database.ExecuteSqlRawAsync(
                "INSERT INTO Vehicles (Id, RegistrationNumber) VALUES (NEWID(), NULL)"));
    }
}
