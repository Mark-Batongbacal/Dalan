using System.Text.Json;
using Dalan.Application.Dispatch;
using Dalan.Contracts.Dispatch;
using Dalan.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Dalan.Infrastructure.Repositories;

public sealed class WarehousePreferenceRepository(DalanDbContext db) : IWarehousePreferenceRepository
{
    private const string PreferenceType = "dalan:preferences:warehouse";
    public async Task<WarehousePreference?> GetAsync(string userId, CancellationToken token = default)
    {
        var value = await db.UserClaims.AsNoTracking().Where(x => x.UserId == userId && x.ClaimType == PreferenceType)
            .OrderByDescending(x => x.Id).Select(x => x.ClaimValue).FirstOrDefaultAsync(token);
        if (value is null) return null;
        try { return JsonSerializer.Deserialize<WarehousePreference>(value); }
        catch (JsonException) { return null; }
    }
    public async Task SaveAsync(string userId, WarehousePreference preference, CancellationToken token = default)
    {
        // A per-account preference in the existing Identity store; never an authorization rule.
        await using var transaction = await PersistenceTransaction.BeginAsync(db, token);
        var previous = await db.UserClaims.Where(x => x.UserId == userId && x.ClaimType == PreferenceType).ToListAsync(token);
        db.UserClaims.RemoveRange(previous);
        db.UserClaims.Add(new IdentityUserClaim<string> { UserId = userId, ClaimType = PreferenceType, ClaimValue = JsonSerializer.Serialize(preference) });
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }
}
