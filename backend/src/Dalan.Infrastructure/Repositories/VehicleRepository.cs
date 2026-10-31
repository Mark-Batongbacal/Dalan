using Dalan.Application.Vehicles;
using Dalan.Domain.Vehicles;
using Dalan.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Dalan.Infrastructure.Repositories;

public sealed class VehicleRepository(DalanDbContext dbContext) : IVehicleRepository
{
    public async Task<AddVehicleOutcome> AddAsync(
        Vehicle vehicle,
        CancellationToken cancellationToken = default)
    {
        dbContext.Vehicles.Add(vehicle);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return AddVehicleOutcome.Added;
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is SqlException sqlException &&
            sqlException.Errors.Cast<SqlError>().Any(error => error.Number is 2601 or 2627))
        {
            // A unique index arbitrates concurrent registrations. Do not leave a failed insert tracked.
            dbContext.Entry(vehicle).State = EntityState.Detached;
            return AddVehicleOutcome.DuplicateRegistration;
        }
    }

    public Task<Vehicle?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => dbContext.Vehicles
            .AsNoTracking()
            .SingleOrDefaultAsync(vehicle => vehicle.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Vehicle>> ListAsync(
        CancellationToken cancellationToken = default)
        => await dbContext.Vehicles
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public async Task<bool> UpdateAsync(
        Vehicle vehicle, CancellationToken cancellationToken = default)
    {
        dbContext.Vehicles.Update(vehicle);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.Entry(vehicle).State = EntityState.Detached;
            return false;
        }
    }

    public Task<bool> HasAssignedDriverAsync(Guid id, CancellationToken cancellationToken = default)
        => dbContext.Drivers.AsNoTracking()
            .AnyAsync(driver => driver.AssignedVehicleId == id, cancellationToken);
}
