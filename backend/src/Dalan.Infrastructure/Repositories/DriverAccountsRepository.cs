using Dalan.Application.Identity;
using Dalan.Contracts.Identity;
using Dalan.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
namespace Dalan.Infrastructure.Repositories;
public sealed class DriverAccountsRepository(DalanDbContext db, UserManager<IdentityUser> users, RoleManager<IdentityRole> roles) : IDriverAccountsRepository
{
    public async Task<DriverAccountState?> Find(Guid id, CancellationToken token)
    {
        var name = await db.Drivers.Where(x => x.Id == id).Select(x => x.Name).SingleOrDefaultAsync(token);
        return name is null ? null : new(name, await db.DriverAccounts.AnyAsync(x => x.DriverId == id, token));
    }
    public async Task<LifecycleResult> Create(DriverAccountProvisionRequest request, CancellationToken token)
    {
        await using var transaction = await PersistenceTransaction.BeginAsync(db, token);
        if (!await db.Drivers.AnyAsync(x => x.Id == request.DriverId, token)) return new(404);
        if (await db.DriverAccounts.AnyAsync(x => x.DriverId == request.DriverId, token)) return new(409);
        try
        {
            if (!await roles.RoleExistsAsync("Driver"))
            {
                var role = await roles.CreateAsync(new("Driver"));
                if (!role.Succeeded) return Invalid(role);
            }
            var email = request.Email.Trim();
            var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            var created = await users.CreateAsync(user, request.Password); if (!created.Succeeded) return Invalid(created);
            var assigned = await users.AddToRoleAsync(user, "Driver"); if (!assigned.Succeeded) return Invalid(assigned);
            db.DriverAccounts.Add(new() { DriverId = request.DriverId, UserId = user.Id }); await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token); return new(201);
        }
        catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 2601 or 2627 }) { return new(409); }
    }
    private static LifecycleResult Invalid(IdentityResult result) => new(400, result.Errors.Select(x => x.Description).ToArray());
}
