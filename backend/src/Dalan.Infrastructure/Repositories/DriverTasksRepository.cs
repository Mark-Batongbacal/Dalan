using System.Security.Cryptography;
using System.Text.Json;
using Dalan.Application.DriverExecution;
using Dalan.Application.Routing;
using Dalan.Contracts.DriverExecution;
using Dalan.Domain.Deliveries;
using Dalan.Domain.Dispatch;
using Dalan.Domain.Drivers;
using Dalan.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Dalan.Infrastructure.Repositories;

public sealed class DriverTasksRepository(DalanDbContext db, TimeProvider clock) : IDriverTasks
{
    public Task<Guid?> FindDriverAsync(string userId, CancellationToken cancellationToken) =>
        (from account in db.DriverAccounts
         join driver in db.Drivers on account.DriverId equals driver.Id
         where account.UserId == userId && driver.Availability != DriverAvailability.Inactive
         select (Guid?)driver.Id).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<DriverTask>> ListAsync(Guid driverId, CancellationToken cancellationToken)
    {
        var deliveries = await ActiveDeliveries(driverId)
            .OrderBy(x => x.AssignedAtUtc).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.RecipientName, x.PickupAddress, x.DropoffAddress,
                x.Status, AssignedAt = x.AssignedAtUtc!.Value,
                x.PickupLatitude, x.PickupLongitude, x.DropoffLatitude, x.DropoffLongitude })
            .ToListAsync(cancellationToken);
        var ids = deliveries.Select(x => x.Id).ToArray();
        var stops = await (
            from plan in db.DispatchPlans
            from route in plan.Routes
            where route.DriverId == driverId
            from stop in route.Stops
            where ids.Contains(stop.DeliveryId)
            select new { stop.DeliveryId, route.Id, stop.Kind, stop.Position, plan.ConfirmedAtUtc })
            .ToListAsync(cancellationToken);
        var byDelivery = stops.ToLookup(x => (x.DeliveryId, x.ConfirmedAtUtc));

        return deliveries.Select(x =>
        {
            var routeStops = byDelivery[(x.Id, x.AssignedAt)];
            return new DriverTask(x.Id, x.RecipientName, x.PickupAddress, x.DropoffAddress,
                x.Status.ToString(), x.AssignedAt, routeStops.FirstOrDefault()?.Id,
                routeStops.FirstOrDefault(s => s.Kind == DispatchStopKind.Pickup)?.Position + 1,
                routeStops.FirstOrDefault(s => s.Kind == DispatchStopKind.Dropoff)?.Position + 1,
                x.PickupLatitude, x.PickupLongitude, x.DropoffLatitude, x.DropoffLongitude);
        }).OrderBy(x => x.AssignedAtUtc)
            .ThenBy(x => x.RouteId)
            .ThenBy(x => x.Status == "PickedUp" ? x.DropoffPosition : x.PickupPosition)
            .ToArray();
    }

    public Task<DriverRouteStops?> GetRouteStopsAsync(Guid driverId, Guid deliveryId,
        CancellationToken cancellationToken) =>
        ActiveDeliveries(driverId).Where(x => x.Id == deliveryId)
            .Select(x => new DriverRouteStops(new GeoPoint(x.PickupLatitude, x.PickupLongitude),
                new GeoPoint(x.DropoffLatitude, x.DropoffLongitude)))
            .SingleOrDefaultAsync(cancellationToken);

    private IQueryable<Delivery> ActiveDeliveries(Guid driverId) =>
        db.Deliveries.AsNoTracking().Where(x => x.DriverId == driverId
            && (x.Status == DeliveryStatus.Assigned || x.Status == DeliveryStatus.PickedUp));

    public Task<byte[]?> GetProofPhotoAsync(Guid deliveryId, CancellationToken cancellationToken) =>
        db.DriverEventReceipts.Where(x => x.DeliveryId == deliveryId && x.Photo != null)
            .Select(x => x.Photo).SingleOrDefaultAsync(cancellationToken);

    public async Task<DriverEventResult> ApplyAsync(Guid driverId, Guid deliveryId,
        DriverTaskEvent request, CancellationToken cancellationToken)
    {
        var delivery = await db.Deliveries.SingleOrDefaultAsync(
            x => x.Id == deliveryId && x.DriverId == driverId, cancellationToken);
        if (delivery is null) return DriverEventResult.Missing;

        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));
        var replay = await ReadReceipt();
        if (replay is not null) return replay.Value;
        if (delivery.AssignedAtUtc != request.AssignedAtUtc) return DriverEventResult.Conflict;
        if (request.EventId == Guid.Empty || request.CapturedAtUtc > clock.GetUtcNow().AddMinutes(5))
            return DriverEventResult.Invalid;

        byte[]? photo = null;
        try
        {
            switch (request.Kind)
            {
                case "Pickup" when request.Photo is null && request.RecipientConfirmation is null:
                    delivery.MarkPickedUp(driverId, request.CapturedAtUtc);
                    break;
                case "Deliver" when request.Latitude is double latitude && request.Longitude is double longitude
                    && request.Photo is { Length: >= 5 and <= 512_000 } jpeg
                    && jpeg[0] == 0xff && jpeg[1] == 0xd8 && jpeg[2] == 0xff
                    && jpeg[^2] == 0xff && jpeg[^1] == 0xd9:
                    delivery.Complete(driverId, $"driver-upload:{request.EventId:D}",
                        request.RecipientConfirmation, latitude, longitude, request.CapturedAtUtc);
                    photo = jpeg;
                    break;
                default:
                    return DriverEventResult.Invalid;
            }
        }
        catch (InvalidOperationException)
        {
            return DriverEventResult.Conflict;
        }
        catch (ArgumentException)
        {
            return DriverEventResult.Invalid;
        }

        db.DriverEventReceipts.Add(new DriverEventReceipt
        {
            DriverId = driverId, EventId = request.EventId, DeliveryId = deliveryId,
            PayloadHash = hash, Photo = photo
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return DriverEventResult.Applied;
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
            || exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return await ReadReceipt() ?? DriverEventResult.Conflict;
        }

        async Task<DriverEventResult?> ReadReceipt()
        {
            var receipt = await db.DriverEventReceipts.AsNoTracking()
                .Where(x => x.DriverId == driverId && x.EventId == request.EventId)
                .Select(x => new { x.DeliveryId, x.PayloadHash })
                .SingleOrDefaultAsync(cancellationToken);
            if (receipt is null) return null;
            return receipt.DeliveryId == deliveryId && receipt.PayloadHash == hash
                ? DriverEventResult.Applied : DriverEventResult.Conflict;
        }
    }
}
