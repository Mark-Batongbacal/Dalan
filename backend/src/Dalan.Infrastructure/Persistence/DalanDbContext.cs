using Dalan.Domain.Deliveries;
using Dalan.Domain.Dispatch;
using Dalan.Domain.Drivers;
using Dalan.Domain.Vehicles;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

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

    public DbSet<DriverAccount> DriverAccounts => Set<DriverAccount>();
    public DbSet<DriverEventReceipt> DriverEventReceipts => Set<DriverEventReceipt>();
    public DbSet<OperationsSnapshot> OperationsSnapshots => Set<OperationsSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
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
