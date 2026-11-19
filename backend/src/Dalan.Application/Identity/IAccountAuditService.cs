using Dalan.Contracts.Identity;
namespace Dalan.Application.Identity;
public interface IAccountAuditService
{
    Task<LifecycleResult> List(Guid? driverId, DateTimeOffset? beforeUtc, int take, CancellationToken cancellationToken, Guid? beforeId = null);
}
