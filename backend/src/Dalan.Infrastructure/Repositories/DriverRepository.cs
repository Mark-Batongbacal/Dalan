using Dalan.Application.Drivers;
using Dalan.Domain.Drivers;
using Dalan.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Dalan.Infrastructure.Repositories;

public sealed class DriverRepository(DalanDbContext dbContext) : IDriverRepository
{
    public async Task<SaveDriverOutcome> AddAsync(
        Driver driver, CancellationToken cancellationToken = default)
    {
        dbContext.Drivers.Add(driver);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return SaveDriverOutcome.Saved;
        }
        catch (DbUpdateException error) when (IsUniqueViolation(error))
        {
            dbContext.Entry(driver).State = EntityState.Detached;
            return SaveDriverOutcome.DuplicateLicense;
        }
    }

    public Task<Driver?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => dbContext.Drivers.AsNoTracking()
            .SingleOrDefaultAsync(driver => driver.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Driver>> ListAsync(CancellationToken cancellationToken = default)
        => await dbContext.Drivers.AsNoTracking()
            .OrderBy(driver => driver.Name)
            .ToListAsync(cancellationToken);

    public async Task<SaveDriverOutcome> UpdateAsync(
        Driver driver, CancellationToken cancellationToken = default)
    {
        dbContext.Drivers.Update(driver);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return SaveDriverOutcome.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.Entry(driver).State = EntityState.Detached;
            return SaveDriverOutcome.Missing;
        }
        catch (DbUpdateException error) when (IsUniqueViolation(error))
        {
            dbContext.Entry(driver).State = EntityState.Detached;
            return error.InnerException!.Message.Contains("IX_Drivers_AssignedVehicleId",
                StringComparison.OrdinalIgnoreCase)
                ? SaveDriverOutcome.VehicleAssigned
                : SaveDriverOutcome.DuplicateLicense;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException error)
        => error.InnerException is SqlException sql
            && sql.Errors.Cast<SqlError>().Any(item => item.Number is 2601 or 2627);
}
