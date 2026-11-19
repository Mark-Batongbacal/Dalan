namespace Dalan.Contracts.Identity;
public sealed record DriverAccountProvisionRequest(Guid DriverId, string Email, string Password);
public sealed record DriverAccountState(string Name, bool Linked);
