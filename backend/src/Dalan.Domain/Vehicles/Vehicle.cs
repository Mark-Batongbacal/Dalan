namespace Dalan.Domain.Vehicles;

public sealed class Vehicle
{
    public const int MaxRegistrationNumberLength = 32;

    private Vehicle() { } // EF Core materialization.

    public Guid Id { get; private set; }
    public string RegistrationNumber { get; private set; } = string.Empty;

    public static Vehicle Register(string? registrationNumber)
    {
        var normalized = registrationNumber?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > MaxRegistrationNumberLength)
        {
            throw new ArgumentException(
                $"Registration number must contain 1 to {MaxRegistrationNumberLength} characters after trimming.",
                nameof(registrationNumber));
        }

        return new Vehicle { Id = Guid.NewGuid(), RegistrationNumber = normalized };
    }
}
