using Dalan.Domain.Deliveries;
using Dalan.Domain.Dispatch;
using Dalan.Domain.Drivers;
using Dalan.Domain.Vehicles;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Dalan.Domain.Tracking;

namespace Dalan.Infrastructure.Persistence;

public sealed class DalanDbContext(DbContextOptions<DalanDbContext> options) : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<Delivery> Deliveries => Set<Delivery>();
    public DbSet<DeliveryProof> DeliveryProofs => Set<DeliveryProof>();
    public DbSet<DispatchPlan> DispatchPlans => Set<DispatchPlan>();
    public DbSet<DispatchRoute> DispatchRoutes => Set<DispatchRoute>();
    public DbSet<DispatchStop> DispatchStops => Set<DispatchStop>();
    public DbSet<DispatchRotationState> DispatchRotations => Set<DispatchRotationState>();

    public DbSet<DriverGpsException> DriverGpsExceptions => Set<DriverGpsException>();
    public DbSet<AccountAccessAudit> AccountAccessAudits => Set<AccountAccessAudit>();
    public DbSet<DriverAccount> DriverAccounts => Set<DriverAccount>();
    public DbSet<DriverEventReceipt> DriverEventReceipts => Set<DriverEventReceipt>();
    public DbSet<OperationsSnapshot> OperationsSnapshots => Set<OperationsSnapshot>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
    public DbSet<VehicleTrackingSession> TrackingSessions => Set<VehicleTrackingSession>();
    public DbSet<VehicleTrackingSample> TrackingSamples => Set<VehicleTrackingSample>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<DriverGpsException>(review =>
        {
            review.HasKey(x => x.Id); review.Property(x => x.RowVersion).IsRowVersion();
            review.Property(x => x.Reason).HasMaxLength(500); review.Property(x => x.ReviewReason).HasMaxLength(500);
            review.Property(x => x.PayloadHash).HasMaxLength(64); review.Property(x => x.Status).HasMaxLength(20);
            review.Property(x => x.ReviewerUserId).HasMaxLength(450);
            review.HasIndex(x => new { x.Status, x.SubmittedAtUtc });
            review.HasOne<Delivery>().WithMany().HasForeignKey(x => x.DeliveryId).OnDelete(DeleteBehavior.Restrict);
            review.HasOne<Driver>().WithMany().HasForeignKey(x => x.DriverId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<VehicleTrackingSession>(session =>
        {
            session.HasKey(x => x.Id);
            session.Property(x => x.RowVersion).IsRowVersion();
            session.HasIndex(x => x.DriverId).IsUnique().HasFilter("[EndedAtUtc] IS NULL");
            session.HasIndex(x => new { x.VehicleId, x.StartedAtUtc });
            session.HasOne<Driver>().WithMany().HasForeignKey(x => x.DriverId).OnDelete(DeleteBehavior.Restrict);
            session.HasOne<Vehicle>().WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<VehicleTrackingSample>(sample =>
        {
            sample.HasKey(x => new { x.SessionId, x.SampleId });
            sample.Property(x => x.PayloadHash).HasMaxLength(64);
            sample.HasIndex(x => x.CapturedAtUtc);
            sample.HasIndex(x => new { x.VehicleId, x.CapturedAtUtc });
            sample.HasOne<VehicleTrackingSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<NotificationDelivery>(mail =>
        {
            mail.HasKey(x => x.Id);
            mail.Property(x => x.Id).HasMaxLength(100);
            mail.Property(x => x.Kind).HasMaxLength(32);
            mail.Property(x => x.From).HasMaxLength(320);
            mail.Property(x => x.Subject).HasMaxLength(300);
            mail.Property(x => x.ProviderId).HasMaxLength(100);
            mail.HasIndex(x => new { x.AcceptedAtUtc, x.NeedsReview, x.NextAttemptAtUtc });
        });
        modelBuilder.Entity<DispatchRotationState>(rotation =>
        {
            rotation.HasKey(x => x.WarehouseKey);
            rotation.Property(x => x.WarehouseKey).HasMaxLength(64);
            rotation.Property(x => x.Version).IsConcurrencyToken();
        });
        modelBuilder.Entity<AccountAccessAudit>(audit =>
        {
            audit.ToTable("AccountAccessAudits");
            audit.HasKey(x => x.Id);
            audit.Property(x => x.Action).HasMaxLength(64).IsRequired();
            audit.Property(x => x.Outcome).HasMaxLength(32).IsRequired();
            audit.Property(x => x.ActorUserId).HasMaxLength(450);
            audit.Property(x => x.TargetUserId).HasMaxLength(450);
            audit.Property(x => x.Reason).HasMaxLength(240);
            audit.Property(x => x.CorrelationId).HasMaxLength(128);
            audit.HasIndex(x => x.OccurredAtUtc);
            audit.HasIndex(x => new { x.DriverId, x.OccurredAtUtc });
            audit.HasIndex(x => new { x.TargetUserId, x.OccurredAtUtc });
        });
        modelBuilder.Entity<DriverAccount>(account =>
        {
            account.HasKey(x => x.DriverId);
            account.HasIndex(x => x.UserId).IsUnique();
            account.HasOne<Driver>().WithOne().HasForeignKey<DriverAccount>(x => x.DriverId);
            account.HasOne<IdentityUser>().WithOne().HasForeignKey<DriverAccount>(x => x.UserId);
        });
        modelBuilder.Entity<DriverEventReceipt>(receipt =>
        {
            receipt.HasKey(x => new { x.DriverId, x.EventId });
            receipt.HasIndex(x => x.DeliveryId);
            receipt.Property(x => x.PayloadHash).HasMaxLength(64);
            receipt.HasOne<Delivery>().WithMany().HasForeignKey(x => x.DeliveryId);
            receipt.HasOne<Driver>().WithMany().HasForeignKey(x => x.DriverId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<OperationsSnapshot>(snapshot =>
        {
            snapshot.Property(x => x.Kind).HasMaxLength(32);
            snapshot.HasKey(x => new { x.DayUtc, x.Kind });
        });

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DalanDbContext).Assembly);
    }
}
