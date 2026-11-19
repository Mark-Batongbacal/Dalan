using Dalan.Contracts.Identity;
namespace Dalan.Application.Identity;
public sealed class DriverRecoveryService(IDriverRecoveryRepository repository) : IDriverRecoveryService
{
    public Task<LifecycleResult> RequestReset(DriverRecoveryEmailRequest request, CancellationToken cancellationToken) => repository.RequestReset(request, cancellationToken);
    public Task<LifecycleResult> Reset(DriverRecoveryPasswordRequest request, CancellationToken cancellationToken) => repository.Reset(request, cancellationToken);
}
