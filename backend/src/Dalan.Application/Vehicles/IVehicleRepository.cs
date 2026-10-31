using Dalan.Domain.Vehicles;

namespace Dalan.Application.Vehicles;

public enum AddVehicleOutcome { Added, DuplicateRegistration }

public interface IVehicleRepository
{
    Task<AddVehicleOutcome> AddAsync(Vehicle vehicle, CancellationToken cancellationToken = default);
    Task<Vehicle?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Vehicle>> ListAsync(CancellationToken cancellationToken = default);
}
