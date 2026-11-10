using Dalan.Domain.Vehicles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dalan.Infrastructure.Persistence.Configurations;

public sealed class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.ToTable("Vehicles");
        builder.HasKey(vehicle => vehicle.Id);
        builder.Property(vehicle => vehicle.Id).ValueGeneratedNever();
        builder.Property(vehicle => vehicle.RegistrationNumber)
            .HasMaxLength(Vehicle.MaxRegistrationNumberLength)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.HasIndex(vehicle => vehicle.RegistrationNumber).IsUnique();
        builder.Property(vehicle => vehicle.Type)
            .HasMaxLength(Vehicle.MaxTypeLength);
        builder.Property(vehicle => vehicle.PayloadCapacityKg)
            .HasPrecision(12, 2);
        builder.Property(vehicle => vehicle.SeatCapacity);
        builder.Property(vehicle => vehicle.Availability)
            .HasConversion<string>()
            .HasMaxLength(16)
            .HasDefaultValue(VehicleAvailability.Available)
            .IsRequired();
        builder.Property(vehicle => vehicle.NextMaintenanceDate)
            .HasColumnType("date");
    }
}
