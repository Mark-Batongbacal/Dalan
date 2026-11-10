namespace Dalan.Domain.Deliveries;

public sealed class DeliveryProof
{
    public const int MaxPhotoReferenceLength = 1024;
    public const int MaxRecipientConfirmationLength = 120;

    private DeliveryProof() { } // EF Core materialization.

    public Guid Id { get; private set; }
    public Guid DeliveryId { get; private set; }
    public string PhotoReference { get; private set; } = string.Empty;
    public string RecipientConfirmation { get; private set; } = string.Empty;
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public DateTimeOffset CapturedAtUtc { get; private set; }

    internal static DeliveryProof Create(
        Guid deliveryId,
        string? photoReference,
        string? recipientConfirmation,
        double latitude,
        double longitude,
        DateTimeOffset capturedAtUtc)
    {
        if (deliveryId == Guid.Empty)
            throw new ArgumentException("Choose a valid delivery.", nameof(deliveryId));
        var normalizedPhoto = photoReference?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedPhoto)
            || normalizedPhoto.Length > MaxPhotoReferenceLength)
            throw new ArgumentException(
                $"Photo reference must contain 1 to {MaxPhotoReferenceLength} characters.",
                nameof(photoReference));
        var normalizedConfirmation = recipientConfirmation?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedConfirmation)
            || normalizedConfirmation.Length > MaxRecipientConfirmationLength)
            throw new ArgumentException(
                $"Recipient confirmation must contain 1 to {MaxRecipientConfirmationLength} characters.",
                nameof(recipientConfirmation));
        Delivery.ValidateCoordinates(latitude, longitude, nameof(latitude), nameof(longitude));

        return new DeliveryProof
        {
            Id = Guid.NewGuid(),
            DeliveryId = deliveryId,
            PhotoReference = normalizedPhoto,
            RecipientConfirmation = normalizedConfirmation,
            Latitude = latitude,
            Longitude = longitude,
            CapturedAtUtc = capturedAtUtc
        };
    }
}
