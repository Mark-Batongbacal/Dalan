using Dalan.Domain.Deliveries;
using Dalan.Domain.Drivers;
using Dalan.Domain.Vehicles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dalan.Infrastructure.Persistence.Configurations;

public sealed class DeliveryConfiguration : IEntityTypeConfiguration<Delivery>
{
    public void Configure(EntityTypeBuilder<Delivery> builder)
    {
        builder.ToTable("Deliveries");
        builder.HasKey(delivery => delivery.Id);
        builder.Property(delivery => delivery.Id).ValueGeneratedNever();
        builder.Property(delivery => delivery.RecipientName)
            .HasMaxLength(Delivery.MaxRecipientNameLength).IsRequired();
        builder.Property(delivery => delivery.PickupAddress)
            .HasMaxLength(Delivery.MaxAddressLength).IsRequired();
        builder.Property(delivery => delivery.DropoffAddress)
            .HasMaxLength(Delivery.MaxAddressLength).IsRequired();
        builder.Property(delivery => delivery.PayloadWeightKg).HasPrecision(12, 2);
        builder.Property(delivery => delivery.Status)
            .HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(delivery => delivery.CreatedAtUtc).IsRequired();
        builder.Property(delivery => delivery.RowVersion).IsRowVersion();

        builder.HasOne<Driver>().WithMany()
            .HasForeignKey(delivery => delivery.DriverId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Vehicle>().WithMany()
            .HasForeignKey(delivery => delivery.VehicleId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(delivery => delivery.Proof).WithOne()
            .HasForeignKey<DeliveryProof>(proof => proof.DeliveryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(delivery => delivery.Status);
        builder.HasIndex(delivery => delivery.CreatedAtUtc);
        builder.HasIndex(delivery => delivery.DriverId);
        builder.HasIndex(delivery => delivery.VehicleId);
    }
}
