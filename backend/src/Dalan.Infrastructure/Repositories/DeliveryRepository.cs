using Dalan.Application.Deliveries;
using Dalan.Domain.Deliveries;
using Dalan.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Dalan.Infrastructure.Repositories;

public sealed class DeliveryRepository(DalanDbContext dbContext) : IDeliveryRepository, IDeliveryBatchRepository
{
    public async Task<IReadOnlyList<Delivery>> ListForPlanningAsync(CancellationToken token = default)
        => await dbContext.Deliveries.AsNoTracking().Include(x=>x.Proof)
            .Where(x=>(x.Status == DeliveryStatus.Pending || x.Status == DeliveryStatus.Assigned)
                && !dbContext.DispatchStops.Any(stop=>stop.DeliveryId == x.Id))
            .OrderByDescending(x=>x.CreatedAtUtc).ToArrayAsync(token);
    public async Task AddRangeAsync(IReadOnlyList<Delivery> deliveries, CancellationToken token = default)
    {
        dbContext.Deliveries.AddRange(deliveries);
        await dbContext.SaveChangesAsync(token);
    }
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

    public Task<bool> IsInConfirmedPlanAsync(
    Guid deliveryId, CancellationToken cancellationToken = default)
    => dbContext.DispatchStops.AsNoTracking()
        .AnyAsync(stop => stop.DeliveryId == deliveryId, cancellationToken);

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
