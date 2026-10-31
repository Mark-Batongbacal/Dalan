namespace Dalan.Domain.Vehicles;

public enum VehicleAvailability { Available, Unavailable, Maintenance, Inactive }

public sealed class Vehicle
{
    public const int MaxRegistrationNumberLength = 32;
    public const int MaxTypeLength = 64;

    private Vehicle() { } // EF Core materialization.

    public Guid Id { get; private set; }
    public string RegistrationNumber { get; private set; } = string.Empty;
    public string? Type { get; private set; }
    public decimal? PayloadCapacityKg { get; private set; }
    public int? SeatCapacity { get; private set; }
    public VehicleAvailability Availability { get; private set; } = VehicleAvailability.Available;
    public DateOnly? NextMaintenanceDate { get; private set; }

    public static Vehicle Register(
        string? registrationNumber, string? type = null, decimal? payloadCapacityKg = null,
        int? seatCapacity = null, DateOnly? nextMaintenanceDate = null)
    {
        var normalized = registrationNumber?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > MaxRegistrationNumberLength)
        {
            throw new ArgumentException(
                $"Registration number must contain 1 to {MaxRegistrationNumberLength} characters after trimming.",
                nameof(registrationNumber));
        }

        var vehicle = new Vehicle { Id = Guid.NewGuid(), RegistrationNumber = normalized };
        vehicle.UpdateDetails(type, payloadCapacityKg, seatCapacity,
            VehicleAvailability.Available, nextMaintenanceDate);
        return vehicle;
    }

    public void UpdateDetails(
        string? type, decimal? payloadCapacityKg, int? seatCapacity,
        VehicleAvailability availability, DateOnly? nextMaintenanceDate)
    {
        var normalizedType = string.IsNullOrWhiteSpace(type) ? null : type.Trim();
        if (normalizedType?.Length > MaxTypeLength)
            throw new ArgumentException($"Vehicle type must be at most {MaxTypeLength} characters.", nameof(type));
        if (payloadCapacityKg is <= 0 or > 1000000)
            throw new ArgumentOutOfRangeException(nameof(payloadCapacityKg), "Payload capacity must be greater than zero and at most 1,000,000 kg.");
        if (seatCapacity is <= 0 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(seatCapacity), "Seat capacity must be between 1 and 1,000.");
        if (!Enum.IsDefined(availability))
            throw new ArgumentOutOfRangeException(nameof(availability), "Choose a valid vehicle availability.");

        Type = normalizedType;
        PayloadCapacityKg = payloadCapacityKg;
        SeatCapacity = seatCapacity;
        Availability = availability;
        NextMaintenanceDate = nextMaintenanceDate;
    }

    public void Deactivate() => Availability = VehicleAvailability.Inactive;
}
