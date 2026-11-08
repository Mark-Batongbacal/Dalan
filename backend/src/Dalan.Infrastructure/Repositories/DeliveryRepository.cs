using Dalan.Application.Deliveries;
using Dalan.Domain.Deliveries;
using Dalan.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Dalan.Infrastructure.Repositories;

public sealed class DeliveryRepository(DalanDbContext dbContext) : IDeliveryRepository
{
    public async Task AddAsync(
        Delivery delivery, CancellationToken cancellationToken = default)
    {
        dbContext.Deliveries.Add(delivery);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<Delivery?> GetByIdAsync(
        Guid id, CancellationToken cancellationToken = default)
        => dbContext.Deliveries.AsNoTracking()
            .Include(delivery => delivery.Proof)
            .SingleOrDefaultAsync(delivery => delivery.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Delivery>> ListAsync(
        CancellationToken cancellationToken = default)
        => await dbContext.Deliveries.AsNoTracking()
            .Include(delivery => delivery.Proof)
            .OrderByDescending(delivery => delivery.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<SaveDeliveryOutcome> UpdateAsync(
        Delivery delivery, CancellationToken cancellationToken = default)
    {
        var proofExists = delivery.Proof is not null
            && await dbContext.DeliveryProofs.AsNoTracking()
                .AnyAsync(proof => proof.DeliveryId == delivery.Id, cancellationToken);
        dbContext.Deliveries.Update(delivery);
        if (delivery.Proof is not null && !proofExists)
            dbContext.Entry(delivery.Proof).State = EntityState.Added;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return SaveDeliveryOutcome.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return await dbContext.Deliveries.AsNoTracking()
                .AnyAsync(item => item.Id == delivery.Id, cancellationToken)
                ? SaveDeliveryOutcome.Conflict
                : SaveDeliveryOutcome.Missing;
        }
    }
}
