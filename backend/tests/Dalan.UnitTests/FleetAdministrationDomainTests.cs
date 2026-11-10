using Dalan.Domain.Drivers;
using Dalan.Domain.Vehicles;
using Xunit;

namespace Dalan.UnitTests;

public sealed class FleetAdministrationDomainTests
{
    [Fact]
    public void Vehicle_details_are_validated_and_deactivation_keeps_history()
    {
        var vehicle = Vehicle.Register(" ABC-123 ", "  Van  ", 500, 3,
            new DateOnly(2027, 1, 10));

        Assert.Equal("Van", vehicle.Type);
        Assert.Equal(500, vehicle.PayloadCapacityKg);
        Assert.Equal(VehicleAvailability.Available, vehicle.Availability);
        Assert.Throws<ArgumentOutOfRangeException>(() => vehicle.UpdateDetails(
            "Van", -1, 3, VehicleAvailability.Available, null));
        Assert.Equal(500, vehicle.PayloadCapacityKg);

        vehicle.Deactivate();
        Assert.Equal(VehicleAvailability.Inactive, vehicle.Availability);
        Assert.Equal("ABC-123", vehicle.RegistrationNumber);
    }

    [Fact]
    public void Driver_requires_valid_identity_and_current_license_for_assignment()
    {
        var driver = Driver.Register("  Jamie Cruz ", " 09123456789 ", " abc-77 ",
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30));
        Assert.Equal("Jamie Cruz", driver.Name);
        Assert.Equal("ABC-77", driver.LicenseNumber);
        var vehicleId = Guid.NewGuid();
        driver.AssignVehicle(vehicleId);
        Assert.Equal(vehicleId, driver.AssignedVehicleId);

        driver.UpdateProfile(driver.Name, driver.Phone, driver.LicenseNumber,
            driver.LicenseExpiry, DriverAvailability.Unavailable);
        Assert.Null(driver.AssignedVehicleId);
        Assert.Throws<InvalidOperationException>(() => driver.AssignVehicle(vehicleId));

        var expired = Driver.Register("Sam", "09000000000", "EXPIRED",
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));
        Assert.Throws<InvalidOperationException>(() => expired.AssignVehicle(vehicleId));
    }
}
