using Dalan.Contracts.Identity;
namespace Dalan.Application.Identity;
public sealed class DriverAccessService(IDriverAccessRepository repository) : IDriverAccessService
{
    public Task<LifecycleResult> Status(Guid id, CancellationToken cancellationToken) => repository.Status(id, cancellationToken);
    public Task<LifecycleResult> Disable(Guid id, DriverAccessChangeRequest request, string actorUserId, CancellationToken cancellationToken) => repository.Disable(id, request, actorUserId, cancellationToken);
    public Task<LifecycleResult> Enable(Guid id, DriverAccessChangeRequest request, string actorUserId, CancellationToken cancellationToken) => repository.Enable(id, request, actorUserId, cancellationToken);
}
