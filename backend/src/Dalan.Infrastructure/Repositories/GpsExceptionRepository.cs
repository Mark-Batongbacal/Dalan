using System.Security.Cryptography;
using System.Text.Json;
using Dalan.Application.DriverExecution;
using Dalan.Application.Identity;
using Dalan.Contracts.DriverExecution;
using Dalan.Domain.Deliveries;
using Dalan.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace Dalan.Infrastructure.Repositories;
public sealed class GpsExceptionRepository(DalanDbContext db, DriverTasksRepository tasks, IConfiguration config, TimeProvider clock) : IGpsExceptionRepository
{
    public async Task<LifecycleResult> Submit(Guid driverId, Guid deliveryId, GpsExceptionRequest request, CancellationToken token)
    {
        if (!bool.TryParse(config["DriverGpsProof:Enabled"], out var enabled) || !enabled) return new(503);
        var proof = request.Proof;
        var reason = request.Reason.Trim();
        if ((proof.Latitude is null) != (proof.Longitude is null)) return new(400);
        if (reason.Length is < 8 or > 500 || proof.Kind != "Deliver" || proof.EventId == Guid.Empty) return new(400);
        var delivery = await db.Deliveries.AsNoTracking().SingleOrDefaultAsync(x => x.Id == deliveryId && x.DriverId == driverId, token);
        if (delivery is null) return new(404);
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { Proof = proof, Reason = reason })));
        var existing = await db.DriverGpsExceptions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == proof.EventId, token);
        if (existing is not null)
            return existing.DriverId != driverId || existing.DeliveryId != deliveryId || existing.PayloadHash != hash
                ? new(409) : new(existing.Status == "Approved" ? 204 : existing.Status == "Rejected" ? 422 : 202);
        if (delivery.AssignedAtUtc != proof.AssignedAtUtc || delivery.Status != DeliveryStatus.PickedUp) return new(409);
        if (proof.CapturedAtUtc < delivery.PickedUpAtUtc || proof.CapturedAtUtc > clock.GetUtcNow().AddMinutes(5)
            || string.IsNullOrWhiteSpace(proof.RecipientConfirmation) || proof.Photo is not { Length: >= 5 and <= 512_000 } photo
            || photo[0] != 255 || photo[1] != 216 || photo[2] != 255 || photo[^2] != 255 || photo[^1] != 217) return new(400);
        db.DriverGpsExceptions.Add(new()
        {
            Id = proof.EventId,
            DriverId = driverId,
            DeliveryId = deliveryId,
            AssignedAtUtc = proof.AssignedAtUtc,
            SubmittedAtUtc = clock.GetUtcNow(),
            Reason = reason,
            Payload = JsonSerializer.Serialize(proof),
            PayloadHash = hash
        });
        try { await db.SaveChangesAsync(token); return new(202); }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); return new(409); }
    }
    public async Task<IReadOnlyList<GpsExceptionItem>> List(CancellationToken token)
    {
        var items = await (from review in db.DriverGpsExceptions.AsNoTracking()
                           join delivery in db.Deliveries.AsNoTracking() on review.DeliveryId equals delivery.Id
                           orderby review.SubmittedAtUtc descending
                           select new { review, delivery.RecipientName, delivery.DropoffLatitude, delivery.DropoffLongitude }).Take(100).ToListAsync(token);
        return items.Select(x =>
        {
            var proof = JsonSerializer.Deserialize<DriverTaskEvent>(x.review.Payload)!;
            return new GpsExceptionItem(x.review.Id, x.review.DriverId, x.review.DeliveryId, x.review.SubmittedAtUtc,
                x.review.Reason, x.review.Status, x.review.ReviewReason, x.review.ReviewerUserId, x.review.ReviewedAtUtc,
                proof.Latitude, proof.Longitude, proof.LocationAccuracyMeters, proof.LocationCapturedAtUtc,
                x.RecipientName, x.DropoffLatitude, x.DropoffLongitude);
        }).ToArray();
    }
    public async Task<byte[]?> Photo(Guid id, CancellationToken token)
    {
        var payload = await db.DriverGpsExceptions.AsNoTracking().Where(x => x.Id == id).Select(x => x.Payload).SingleOrDefaultAsync(token);
        return payload is null ? null : JsonSerializer.Deserialize<DriverTaskEvent>(payload)?.Photo;
    }
    public async Task<LifecycleResult> Review(Guid id, GpsExceptionReview request, string actor, CancellationToken token)
    {
        if (request.Reason.Trim().Length is < 8 or > 500) return new(400);
        await using var transaction = await PersistenceTransaction.BeginAsync(db, token);
        var review = await db.DriverGpsExceptions.FromSqlInterpolated($"SELECT * FROM DriverGpsExceptions WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id}").SingleOrDefaultAsync(token);
        if (review is null) return new(404);
        if (review.Status != "Pending") return new(409);
        var delivery = await db.Deliveries.AsNoTracking().SingleAsync(x => x.Id == review.DeliveryId, token);
        if (delivery.DriverId != review.DriverId || delivery.AssignedAtUtc != review.AssignedAtUtc || delivery.Status != DeliveryStatus.PickedUp) return new(409);
        if (request.Approve)
        {
            var result = await tasks.ApplyReviewedAsync(review.DriverId, review.DeliveryId,
                JsonSerializer.Deserialize<DriverTaskEvent>(review.Payload)!, token);
            if (result != DriverEventResult.Applied) return new(409);
        }
        review.Status = request.Approve ? "Approved" : "Rejected"; review.ReviewReason = request.Reason.Trim();
        review.ReviewerUserId = actor; review.ReviewedAtUtc = clock.GetUtcNow();
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token); return new(200);
    }
}
