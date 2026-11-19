using Dalan.Contracts.Identity;
namespace Dalan.Application.Identity;
public interface IDriverRecoveryService
{
    Task<LifecycleResult> RequestReset(DriverRecoveryEmailRequest request, CancellationToken cancellationToken);
    Task<LifecycleResult> Reset(DriverRecoveryPasswordRequest request, CancellationToken cancellationToken);
}
