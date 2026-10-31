using Dalan.Domain.Drivers;

namespace Dalan.Application.Drivers;

public enum SaveDriverOutcome { Saved, Missing, DuplicateLicense, VehicleAssigned }

public interface IDriverRepository
{
    Task<SaveDriverOutcome> AddAsync(Driver driver, CancellationToken cancellationToken = default);
    Task<Driver?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Driver>> ListAsync(CancellationToken cancellationToken = default);
    Task<SaveDriverOutcome> UpdateAsync(Driver driver, CancellationToken cancellationToken = default);
}
