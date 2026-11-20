using Dalan.Contracts.Identity;
namespace Dalan.Application.Identity;
public sealed class DriverInvitationsService(IDriverInvitationsRepository repository) : IDriverInvitationsService
{
    public Task<LifecycleResult> Issue(DriverInvitationIssueRequest request, CancellationToken cancellationToken) => repository.Issue(request, cancellationToken);
    public Task<LifecycleResult> Activate(DriverInvitationActivationRequest request, CancellationToken cancellationToken) => repository.Activate(request, cancellationToken);
}
