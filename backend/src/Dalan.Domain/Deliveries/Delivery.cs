namespace Dalan.Domain.Deliveries;

public enum DeliveryStatus
{
    Pending,
    Assigned,
    PickedUp,
    Delivered,
    Cancelled
}

public sealed class Delivery
{
    public const int MaxRecipientNameLength = 120;
    public const int MaxAddressLength = 500;
    public const decimal MaxPayloadWeightKg = 1_000_000;

    private Delivery() { } // EF Core materialization.

    public Guid Id { get; private set; }
    public string RecipientName { get; private set; } = string.Empty;
    public string PickupAddress { get; private set; } = string.Empty;
    public double PickupLatitude { get; private set; }
    public double PickupLongitude { get; private set; }
    public string DropoffAddress { get; private set; } = string.Empty;
    public double DropoffLatitude { get; private set; }
    public double DropoffLongitude { get; private set; }
    public decimal? PayloadWeightKg { get; private set; }
    public DateTimeOffset? ExpectedDeliveryAtUtc { get; private set; }
    public Guid? DriverId { get; private set; }
    public Guid? VehicleId { get; private set; }
    public DeliveryStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? AssignedAtUtc { get; private set; }
    public DateTimeOffset? PickedUpAtUtc { get; private set; }
    public DateTimeOffset? DeliveredAtUtc { get; private set; }
    public DateTimeOffset? CancelledAtUtc { get; private set; }
    public DeliveryProof? Proof { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static Delivery Create(
        string? recipientName,
        string? pickupAddress,
        double pickupLatitude,
        double pickupLongitude,
        string? dropoffAddress,
        double dropoffLatitude,
        double dropoffLongitude,
        DateTimeOffset createdAt,
        decimal? payloadWeightKg = null,
        DateTimeOffset? expectedDeliveryAt = null)
    {
        var normalizedCreatedAt = RequireTime(createdAt, nameof(createdAt));
        ValidateCoordinates(pickupLatitude, pickupLongitude,
            nameof(pickupLatitude), nameof(pickupLongitude));
        ValidateCoordinates(dropoffLatitude, dropoffLongitude,
            nameof(dropoffLatitude), nameof(dropoffLongitude));
        if (payloadWeightKg is <= 0 or > MaxPayloadWeightKg)
            throw new ArgumentOutOfRangeException(nameof(payloadWeightKg),
                $"Payload weight must be greater than zero and at most {MaxPayloadWeightKg:N0} kg.");

        var normalizedExpectedAt = expectedDeliveryAt?.ToUniversalTime();
        if (normalizedExpectedAt is not null && normalizedExpectedAt <= normalizedCreatedAt)
            throw new ArgumentException("Expected delivery time must be later than creation time.",
                nameof(expectedDeliveryAt));

        return new Delivery
        {
            Id = Guid.NewGuid(),
            RecipientName = NormalizeRequiredText(recipientName,
                MaxRecipientNameLength, nameof(recipientName)),
            PickupAddress = NormalizeRequiredText(pickupAddress,
                MaxAddressLength, nameof(pickupAddress)),
            PickupLatitude = pickupLatitude,
            PickupLongitude = pickupLongitude,
            DropoffAddress = NormalizeRequiredText(dropoffAddress,
                MaxAddressLength, nameof(dropoffAddress)),
            DropoffLatitude = dropoffLatitude,
            DropoffLongitude = dropoffLongitude,
            PayloadWeightKg = payloadWeightKg,
            ExpectedDeliveryAtUtc = normalizedExpectedAt,
            Status = DeliveryStatus.Pending,
            CreatedAtUtc = normalizedCreatedAt
        };
    }

    public void Assign(Guid driverId, Guid vehicleId, DateTimeOffset assignedAt)
    {
        if (Status is not (DeliveryStatus.Pending or DeliveryStatus.Assigned))
            throw new InvalidOperationException($"A delivery with status {Status} cannot be assigned.");
        if (driverId == Guid.Empty)
            throw new ArgumentException("Choose a valid driver.", nameof(driverId));
        if (vehicleId == Guid.Empty)
            throw new ArgumentException("Choose a valid vehicle.", nameof(vehicleId));

        var normalizedAssignedAt = RequireTimeAtOrAfter(
            assignedAt, AssignedAtUtc ?? CreatedAtUtc, nameof(assignedAt));
        DriverId = driverId;
        VehicleId = vehicleId;
        AssignedAtUtc = normalizedAssignedAt;
        Status = DeliveryStatus.Assigned;
    }

    public void MarkPickedUp(Guid driverId, DateTimeOffset pickedUpAt)
    {
        if (Status != DeliveryStatus.Assigned)
            throw new InvalidOperationException($"A delivery with status {Status} cannot be picked up.");
        EnsureAssignedDriver(driverId);
        PickedUpAtUtc = RequireTimeAtOrAfter(
            pickedUpAt, AssignedAtUtc!.Value, nameof(pickedUpAt));
        Status = DeliveryStatus.PickedUp;
    }

    public void Complete(
        Guid driverId,
        string? photoReference,
        string? recipientConfirmation,
        double latitude,
        double longitude,
        DateTimeOffset capturedAt)
    {
        if (Status != DeliveryStatus.PickedUp)
            throw new InvalidOperationException($"A delivery with status {Status} cannot be completed.");
        EnsureAssignedDriver(driverId);
        var normalizedCapturedAt = RequireTimeAtOrAfter(
            capturedAt, PickedUpAtUtc!.Value, nameof(capturedAt));
        Proof = DeliveryProof.Create(Id, photoReference, recipientConfirmation,
            latitude, longitude, normalizedCapturedAt);
        DeliveredAtUtc = normalizedCapturedAt;
        Status = DeliveryStatus.Delivered;
    }

    public void Cancel(DateTimeOffset cancelledAt)
    {
        if (Status is not (DeliveryStatus.Pending or DeliveryStatus.Assigned))
            throw new InvalidOperationException($"A delivery with status {Status} cannot be cancelled.");
        CancelledAtUtc = RequireTimeAtOrAfter(
            cancelledAt, AssignedAtUtc ?? CreatedAtUtc, nameof(cancelledAt));
        Status = DeliveryStatus.Cancelled;
    }

    private void EnsureAssignedDriver(Guid driverId)
    {
        if (driverId == Guid.Empty || DriverId != driverId)
            throw new InvalidOperationException("Only the assigned driver can update this delivery.");
    }

    private static string NormalizeRequiredText(
        string? value, int maximumLength, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maximumLength)
            throw new ArgumentException(
                $"{parameterName} must contain 1 to {maximumLength} characters after trimming.",
                parameterName);
        return normalized;
    }

    internal static void ValidateCoordinates(
        double latitude, double longitude,
        string latitudeParameterName, string longitudeParameterName)
    {
        if (!double.IsFinite(latitude) || latitude is < -90 or > 90)
            throw new ArgumentOutOfRangeException(latitudeParameterName,
                "Latitude must be between -90 and 90.");
        if (!double.IsFinite(longitude) || longitude is < -180 or > 180)
            throw new ArgumentOutOfRangeException(longitudeParameterName,
                "Longitude must be between -180 and 180.");
    }

    private static DateTimeOffset RequireTime(DateTimeOffset value, string parameterName)
    {
        if (value == default)
            throw new ArgumentException("A valid timestamp is required.", parameterName);
        return value.ToUniversalTime();
    }

    private static DateTimeOffset RequireTimeAtOrAfter(
        DateTimeOffset value, DateTimeOffset minimum, string parameterName)
    {
        var normalized = RequireTime(value, parameterName);
        if (normalized < minimum)
            throw new ArgumentException(
                "Timestamp cannot be earlier than the previous lifecycle event.", parameterName);
        return normalized;
    }
}
