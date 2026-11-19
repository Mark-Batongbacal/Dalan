using Dalan.Contracts.Identity;
namespace Dalan.Application.Identity;
public interface IDriverInvitationsService
{
    Task<LifecycleResult> Issue(DriverInvitationIssueRequest request, CancellationToken cancellationToken);
    Task<LifecycleResult> Activate(DriverInvitationActivationRequest request, CancellationToken cancellationToken);
}
