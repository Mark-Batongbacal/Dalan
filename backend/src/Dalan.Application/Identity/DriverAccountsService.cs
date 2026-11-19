using Dalan.Contracts.Identity;
namespace Dalan.Application.Identity;
public interface IDriverAccountsService
{
    Task<DriverAccountState?> Find(Guid id, CancellationToken token);
    Task<LifecycleResult> Create(DriverAccountProvisionRequest request, CancellationToken token);
}
public interface IDriverAccountsRepository : IDriverAccountsService { }
public sealed class DriverAccountsService(IDriverAccountsRepository repository) : IDriverAccountsService
{
    public Task<DriverAccountState?> Find(Guid id, CancellationToken token) => repository.Find(id, token);
    public Task<LifecycleResult> Create(DriverAccountProvisionRequest request, CancellationToken token) => repository.Create(request, token);
}
