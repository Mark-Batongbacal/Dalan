using Dalan.Contracts.Identity;
namespace Dalan.Application.Identity;
public interface IDriverAccessService
{
    Task<LifecycleResult> Status(Guid id, CancellationToken cancellationToken);
    Task<LifecycleResult> Disable(Guid id, DriverAccessChangeRequest request, string actorUserId, CancellationToken cancellationToken);
    Task<LifecycleResult> Enable(Guid id, DriverAccessChangeRequest request, string actorUserId, CancellationToken cancellationToken);
}
