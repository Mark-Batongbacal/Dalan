namespace Dalan.Domain.Drivers;

public enum DriverAvailability { Available, Unavailable, Inactive }

public sealed class Driver
{
    public const int MaxNameLength = 120;
    public const int MaxPhoneLength = 32;
    public const int MaxLicenseNumberLength = 64;

    private Driver() { } // EF Core materialization.

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;
    public string LicenseNumber { get; private set; } = string.Empty;
    public DateOnly LicenseExpiry { get; private set; }
    public DriverAvailability Availability { get; private set; } = DriverAvailability.Available;
    public Guid? AssignedVehicleId { get; private set; }

    public static Driver Register(string? name, string? phone, string? licenseNumber,
        DateOnly licenseExpiry)
    {
        var driver = new Driver { Id = Guid.NewGuid() };
        driver.UpdateProfile(name, phone, licenseNumber, licenseExpiry,
            DriverAvailability.Available);
        return driver;
    }

    public void UpdateProfile(string? name, string? phone, string? licenseNumber,
        DateOnly licenseExpiry, DriverAvailability availability)
    {
        var normalizedName = name?.Trim();
        var normalizedPhone = phone?.Trim();
        var normalizedLicense = licenseNumber?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedName) || normalizedName.Length > MaxNameLength)
            throw new ArgumentException($"Driver name must contain 1 to {MaxNameLength} characters.", nameof(name));
        if (string.IsNullOrWhiteSpace(normalizedPhone) || normalizedPhone.Length > MaxPhoneLength)
            throw new ArgumentException($"Phone must contain 1 to {MaxPhoneLength} characters.", nameof(phone));
        if (string.IsNullOrWhiteSpace(normalizedLicense) || normalizedLicense.Length > MaxLicenseNumberLength)
            throw new ArgumentException($"License number must contain 1 to {MaxLicenseNumberLength} characters.", nameof(licenseNumber));
        if (licenseExpiry == default)
            throw new ArgumentException("License expiry is required.", nameof(licenseExpiry));
        if (!Enum.IsDefined(availability))
            throw new ArgumentOutOfRangeException(nameof(availability));
        if (AssignedVehicleId is not null && availability == DriverAvailability.Available
            && licenseExpiry < DateOnly.FromDateTime(DateTime.UtcNow))
            throw new ArgumentException("An assigned driver must have a current license.", nameof(licenseExpiry));

        Name = normalizedName;
        Phone = normalizedPhone;
        LicenseNumber = normalizedLicense;
        LicenseExpiry = licenseExpiry;
        Availability = availability;
        if (availability != DriverAvailability.Available)
            AssignedVehicleId = null;
    }

    public void AssignVehicle(Guid? vehicleId)
    {
        if (vehicleId == Guid.Empty)
            throw new ArgumentException("Choose a valid vehicle.", nameof(vehicleId));
        if (vehicleId is not null && Availability != DriverAvailability.Available)
            throw new InvalidOperationException("Only an available driver can be assigned a vehicle.");
        if (vehicleId is not null && LicenseExpiry < DateOnly.FromDateTime(DateTime.UtcNow))
            throw new InvalidOperationException("A valid license is required for vehicle assignment.");
        AssignedVehicleId = vehicleId;
    }

    public void Deactivate()
    {
        Availability = DriverAvailability.Inactive;
        AssignedVehicleId = null;
    }
}
