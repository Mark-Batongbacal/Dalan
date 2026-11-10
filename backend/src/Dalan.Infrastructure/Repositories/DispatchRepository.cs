using System.Data;
using Dalan.Application.Dispatch;
using Dalan.Domain.Deliveries;
using Dalan.Domain.Dispatch;
using Dalan.Domain.Drivers;
using Dalan.Domain.Vehicles;
using Dalan.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Dalan.Infrastructure.Repositories;

public sealed class DispatchRepository(DalanDbContext db) : IDispatchRepository
{
    public async Task<DispatchCandidates> ReadCandidatesAsync(
        IReadOnlyList<Guid> deliveryIds,
        IReadOnlyList<Guid> vehicleIds,
        CancellationToken cancellationToken = default)
    {
        var deliveries = await db.Deliveries.AsNoTracking()
            .Where(delivery => deliveryIds.Contains(delivery.Id))
            .ToArrayAsync(cancellationToken);

        var vehicles = await db.Vehicles.AsNoTracking()
            .Where(vehicle => vehicleIds.Contains(vehicle.Id))
            .ToArrayAsync(cancellationToken);

        var drivers = await db.Drivers.AsNoTracking()
            .Where(driver => driver.AssignedVehicleId != null &&
                vehicleIds.Contains(driver.AssignedVehicleId.Value))
            .ToArrayAsync(cancellationToken);

        var dispatchedIds = await db.DispatchStops.AsNoTracking()
            .Where(stop => deliveryIds.Contains(stop.DeliveryId))
            .Select(stop => stop.DeliveryId)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        return new DispatchCandidates(
            deliveries,
            vehicles,
            drivers,
            dispatchedIds.ToHashSet());
    }

    public Task<DispatchPlan?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => db.DispatchPlans.AsNoTracking()
            .Include(plan => plan.Routes)
            .ThenInclude(route => route.Stops)
            .SingleOrDefaultAsync(plan => plan.Id == id, cancellationToken);

    public async Task<bool> ConfirmAsync(
        DispatchPlan plan,
        IReadOnlyList<DispatchDeliveryVersion> expectedVersions,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var expected = expectedVersions.ToDictionary(
           version => version.DeliveryId,
           version => version.RowVersion);
            var deliveryIds = expected.Keys.ToArray();
            var vehicleIds = plan.Routes.Select(route => route.VehicleId).ToArray();
            var driverIds = plan.Routes.Select(route => route.DriverId).ToArray();

            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);

            var deliveries = await db.Deliveries
                .Where(delivery => deliveryIds.Contains(delivery.Id))
                .ToDictionaryAsync(delivery => delivery.Id, cancellationToken);
            var vehicles = await db.Vehicles.AsNoTracking()
                .Where(vehicle => vehicleIds.Contains(vehicle.Id))
                .ToDictionaryAsync(vehicle => vehicle.Id, cancellationToken);
            var drivers = await db.Drivers.AsNoTracking()
                .Where(driver => driverIds.Contains(driver.Id))
                .ToDictionaryAsync(driver => driver.Id, cancellationToken);

            if (deliveries.Count != deliveryIds.Length ||
                vehicles.Count != vehicleIds.Length ||
                drivers.Count != driverIds.Length ||
                deliveries.Any(pair =>
                    !pair.Value.RowVersion.SequenceEqual(expected[pair.Key]) ||
                    pair.Value.Status is not
                        (DeliveryStatus.Pending or DeliveryStatus.Assigned)) ||
                await db.DispatchStops.AnyAsync(
                    stop => deliveryIds.Contains(stop.DeliveryId),
                    cancellationToken))
            {
                return false;
            }

            var today = DateOnly.FromDateTime(plan.ConfirmedAtUtc.UtcDateTime);
            var previouslyAssigned = new List<Delivery>();
            foreach (var route in plan.Routes)
            {
                var vehicle = vehicles[route.VehicleId];
                var driver = drivers[route.DriverId];

                if (vehicle.Availability != VehicleAvailability.Available ||
                    driver.Availability != DriverAvailability.Available ||
                    driver.LicenseExpiry < today ||
                    driver.AssignedVehicleId != vehicle.Id)
                    return false;

                foreach (var deliveryId in route.Stops
                    .Select(stop => stop.DeliveryId).Distinct())
                {
                    var delivery = deliveries[deliveryId];

                    if (delivery.Status == DeliveryStatus.Assigned)
                    {
                        if (delivery.VehicleId != vehicle.Id ||
                            delivery.DriverId != driver.Id)
                            return false;
                        previouslyAssigned.Add(delivery);
                    }
                    else
                    {
                        delivery.Assign(driver.Id, vehicle.Id, plan.ConfirmedAtUtc);
                    }

                    if (delivery.PayloadWeightKg is decimal weight &&
                        vehicle.PayloadCapacityKg is decimal capacity &&
                        weight > capacity)
                        return false;
                }
            }

            foreach (var delivery in previouslyAssigned)
                db.Entry(delivery).Property(item => item.AssignedAtUtc).IsModified = true;

            db.DispatchPlans.Add(plan);

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                return false;
            }
            catch (DbUpdateException error)
                when (error.InnerException is SqlException sql &&
                      sql.Number is 2601 or 2627)
            {
                return false;
            }

        }
        catch (Exception error) when ( error is SqlException { Number: 1205 } || error is DbUpdateException { 
            InnerException: SqlException { Number: 1205 }
            })
        {
            return false;
        }

    }
}