namespace Dalan.Contracts.Identity;
public sealed record DriverMobileAccountStatus(Guid DriverId, string MobileAccess, string? Email = null);
