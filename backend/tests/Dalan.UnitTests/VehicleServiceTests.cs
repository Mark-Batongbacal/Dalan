using Dalan.Application.Vehicles;
using Dalan.Domain.Vehicles;
using Xunit;

namespace Dalan.UnitTests;

public sealed class VehicleServiceTests
{
    [Fact]
    public async Task Registration_passes_normalized_entity_and_cancellation_to_repository()
    {
        var repository = new FakeRepository();
        var service = new VehicleService(repository);
        using var source = new CancellationTokenSource();

        var result = await service.RegisterAsync(new(" abc-123 "), source.Token);

        Assert.Equal(VehicleRegistrationStatus.Registered, result.Status);
        Assert.Equal("ABC-123", result.Vehicle!.RegistrationNumber);
        Assert.Equal(repository.Stored!.Id, result.Vehicle.Id);
        Assert.Equal(source.Token, repository.Token);
    }

    [Fact]
    public async Task Invalid_input_does_not_call_repository()
    {
        var repository = new FakeRepository();
        var result = await new VehicleService(repository).RegisterAsync(new("   "));

        Assert.Equal(VehicleRegistrationStatus.Invalid, result.Status);
        Assert.Null(repository.Stored);
    }

    [Fact]
    public async Task Duplicate_is_reported_without_a_success_payload()
    {
        var repository = new FakeRepository { Outcome = AddVehicleOutcome.DuplicateRegistration };
        var result = await new VehicleService(repository).RegisterAsync(new("ABC-123"));

        Assert.Equal(VehicleRegistrationStatus.Duplicate, result.Status);
        Assert.Null(result.Vehicle);
    }

    [Fact]
    public async Task Retrieval_maps_entity_and_missing_id_returns_null()
    {
        var vehicle = Vehicle.Register("ABC-123");
        var repository = new FakeRepository { Stored = vehicle };
        var service = new VehicleService(repository);

        Assert.Equal(new VehicleDetails(vehicle.Id, "ABC-123"), await service.GetByIdAsync(vehicle.Id));
        Assert.Null(await service.GetByIdAsync(Guid.NewGuid()));
    }

    private sealed class FakeRepository : IVehicleRepository
    {
        public Vehicle? Stored { get; set; }
        public CancellationToken Token { get; private set; }
        public AddVehicleOutcome Outcome { get; init; } = AddVehicleOutcome.Added;

        public Task<AddVehicleOutcome> AddAsync(Vehicle vehicle, CancellationToken cancellationToken = default)
        {
            Stored = vehicle;
            Token = cancellationToken;
            return Task.FromResult(Outcome);
        }

        public Task<Vehicle?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;
            return Task.FromResult(Stored?.Id == id ? Stored : null);
        }
    }
}
