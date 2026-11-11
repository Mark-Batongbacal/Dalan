using System.Data;
using Dalan.Application.Dispatch;
using Dalan.Application.FleetOptimization;
using Dalan.Application.Routing;
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
    public async Task<DispatchRotation> ReadRotationAsync(string warehouseKey, CancellationToken cancellationToken = default)
    {
        var state = await db.DispatchRotations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.WarehouseKey == warehouseKey, cancellationToken);
        return new(state?.Version ?? 0, state?.LastFirstDriverId);
    }

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

            DispatchRotationState? rotation = null;
            if (plan.Strategy == "WarehouseClusterRotation")
            {
                rotation = await db.DispatchRotations.FromSqlInterpolated(
                    $"SELECT [WarehouseKey], [Version], [LastFirstDriverId] FROM [DispatchRotations] WITH (UPDLOCK, HOLDLOCK) WHERE [WarehouseKey] = {plan.WarehouseKey}")
                    .SingleOrDefaultAsync(cancellationToken);
                if ((rotation?.Version ?? 0) != plan.RotationVersion) return false;
                var order = new DispatchRotation(rotation?.Version ?? 0, rotation?.LastFirstDriverId)
                    .OrderRiders(plan.Routes.Select(x => x.DriverId));
                if (order[0] != plan.RotationFirstDriverId) return false;
                if (rotation is null)
                {
                    rotation = new DispatchRotationState { WarehouseKey = plan.WarehouseKey! };
                    db.DispatchRotations.Add(rotation);
                }
            }

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

                if (plan.Strategy != "MinimumTotalTravelTime")
                {
                    if (route.Stops.Count > 0 && vehicle.PayloadCapacityKg is null) return false;
                    if (plan.Strategy == "WarehouseClusterRotation"
                        && FleetOptimizer.WarehouseKey(new GeoPoint(route.StartLatitude, route.StartLongitude)) != plan.WarehouseKey)
                        return false;
                    var load = 0m;
                    var departed = false;
                    foreach (var stop in route.Stops.OrderBy(x => x.Position))
                    {
                        if (!deliveries.TryGetValue(stop.DeliveryId, out var item)
                            || item.PayloadWeightKg is not decimal weight) return false;
                        if (plan.Strategy == "WarehouseClusterRotation")
                        {
                            if (FleetOptimizer.WarehouseKey(new GeoPoint(item.PickupLatitude, item.PickupLongitude)) != plan.WarehouseKey
                                || stop.Kind == DispatchStopKind.Pickup && departed) return false;
                            if (stop.Kind == DispatchStopKind.Dropoff) departed = true;
                        }
                        load += stop.Kind == DispatchStopKind.Pickup ? weight : -weight;
                        if (load < 0 || load > vehicle.PayloadCapacityKg) return false;
                    }
                }

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

            if (plan.MaxFleetTravelSeconds is double budget && plan.Routes.Sum(x => x.DurationSeconds) > budget + 0.001)
                return false;
            db.DispatchPlans.Add(plan);
            if (rotation is not null)
            {
                rotation.Version++;
                rotation.LastFirstDriverId = plan.RotationFirstDriverId;
            }

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
        catch (Exception error) when (IsSerializationConflict(error))
        {
            return false;
        }

    }

    private static bool IsSerializationConflict(Exception error)
    {
        // EF's execution strategy can wrap the SQL deadlock in InvalidOperationException.
        for (Exception? current = error; current is not null; current = current.InnerException)
            if (current is SqlException { Number: 1205 }) return true;
        return false;
    }
}
