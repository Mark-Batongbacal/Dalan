using Dalan.Domain.Deliveries;
using Dalan.Domain.Dispatch;
using Dalan.Domain.Drivers;
using Dalan.Domain.Vehicles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dalan.Infrastructure.Persistence.Configurations;

public sealed class DispatchPlanConfiguration
    : IEntityTypeConfiguration<DispatchPlan>
{
    public void Configure(EntityTypeBuilder<DispatchPlan> builder)
    {
        builder.ToTable("DispatchPlans");
        builder.HasKey(plan => plan.Id);
        builder.Property(plan => plan.Id).ValueGeneratedNever();
        builder.Property(plan => plan.Strategy).HasMaxLength(32).HasDefaultValue("MinimumTotalTravelTime");
        builder.Property(plan => plan.WarehouseKey).HasMaxLength(64);

        builder.HasMany(plan => plan.Routes)
            .WithOne()
            .HasForeignKey("PlanId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class DispatchRouteConfiguration
    : IEntityTypeConfiguration<DispatchRoute>
{
    public void Configure(EntityTypeBuilder<DispatchRoute> builder)
    {
        builder.ToTable("DispatchRoutes");
        builder.HasKey(route => route.Id);
        builder.Property(route => route.Id).ValueGeneratedNever();

        builder.HasIndex("PlanId", nameof(DispatchRoute.VehicleId)).IsUnique();

        builder.HasOne<Vehicle>().WithMany()
            .HasForeignKey(route => route.VehicleId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Driver>().WithMany()
            .HasForeignKey(route => route.DriverId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(route => route.Stops)
            .WithOne()
            .HasForeignKey("RouteId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class DispatchStopConfiguration
    : IEntityTypeConfiguration<DispatchStop>
{
    public void Configure(EntityTypeBuilder<DispatchStop> builder)
    {
        builder.ToTable("DispatchStops");
        builder.HasKey(stop => stop.Id);
        builder.Property(stop => stop.Id).ValueGeneratedNever();
        builder.Property(stop => stop.Kind)
            .HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.HasIndex("RouteId", nameof(DispatchStop.Position)).IsUnique();

        // A delivery may appear in only one confirmed plan.
        builder.HasIndex(stop => new { stop.DeliveryId, stop.Kind }).IsUnique();

        builder.HasOne<Delivery>().WithMany()
            .HasForeignKey(stop => stop.DeliveryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
