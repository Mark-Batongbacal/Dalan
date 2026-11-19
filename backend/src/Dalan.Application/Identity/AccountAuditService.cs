using Dalan.Contracts.Identity;
namespace Dalan.Application.Identity;
public sealed class AccountAuditService(IAccountAuditRepository repository) : IAccountAuditService
{
    public Task<LifecycleResult> List(Guid? driverId, DateTimeOffset? beforeUtc, int take, CancellationToken cancellationToken, Guid? beforeId = null) => repository.List(driverId, beforeUtc, take, cancellationToken, beforeId);
}
