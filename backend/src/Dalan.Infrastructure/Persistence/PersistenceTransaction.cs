using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
namespace Dalan.Infrastructure.Persistence;
// Inner account operations use savepoints when an audited request already owns a transaction.
public sealed class PersistenceTransaction : IAsyncDisposable
{
    private readonly IDbContextTransaction transaction;
    private readonly string? savepoint;
    private bool committed;
    private PersistenceTransaction(IDbContextTransaction transaction, string? savepoint)
    { this.transaction = transaction; this.savepoint = savepoint; }
    public static async Task<PersistenceTransaction> BeginAsync(DalanDbContext db, CancellationToken token)
    {
        if (db.Database.CurrentTransaction is not { } existing)
            return new(await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token), null);
        var name = "account_" + Guid.NewGuid().ToString("N")[..24];
        await existing.CreateSavepointAsync(name, token);
        return new(existing, name);
    }
    public async Task CommitAsync(CancellationToken token)
    {
        if (savepoint is null) await transaction.CommitAsync(token);
        committed = true;
    }
    public async ValueTask DisposeAsync()
    {
        if (savepoint is null) await transaction.DisposeAsync();
        else if (!committed) await transaction.RollbackToSavepointAsync(savepoint);
    }
}
