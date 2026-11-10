using Dalan.Domain.Drivers;
using Dalan.Domain.Vehicles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dalan.Infrastructure.Persistence.Configurations;

public sealed class DriverConfiguration : IEntityTypeConfiguration<Driver>
{
    public void Configure(EntityTypeBuilder<Driver> builder)
    {
        builder.ToTable("Drivers");
        builder.HasKey(driver => driver.Id);
        builder.Property(driver => driver.Id).ValueGeneratedNever();
        builder.Property(driver => driver.Name).HasMaxLength(Driver.MaxNameLength).IsRequired();
        builder.Property(driver => driver.Phone).HasMaxLength(Driver.MaxPhoneLength).IsRequired();
        builder.Property(driver => driver.LicenseNumber)
            .HasMaxLength(Driver.MaxLicenseNumberLength)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.HasIndex(driver => driver.LicenseNumber).IsUnique();
        builder.Property(driver => driver.LicenseExpiry).HasColumnType("date").IsRequired();
        builder.Property(driver => driver.Availability)
            .HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.HasOne<Vehicle>().WithMany().HasForeignKey(driver => driver.AssignedVehicleId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(driver => driver.AssignedVehicleId)
            .IsUnique().HasFilter("[AssignedVehicleId] IS NOT NULL");
    }
}
