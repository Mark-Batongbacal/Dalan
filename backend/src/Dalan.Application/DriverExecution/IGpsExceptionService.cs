using Dalan.Application.Identity;
using Dalan.Contracts.DriverExecution;
namespace Dalan.Application.DriverExecution;
public interface IGpsExceptionService
{
    Task<LifecycleResult> Submit(Guid driverId, Guid deliveryId, GpsExceptionRequest request, CancellationToken token);
    Task<IReadOnlyList<GpsExceptionItem>> List(CancellationToken token);
    Task<LifecycleResult> Review(Guid id, GpsExceptionReview request, string actor, CancellationToken token);
    Task<byte[]?> Photo(Guid id, CancellationToken token);
}
public interface IGpsExceptionRepository : IGpsExceptionService { }
public sealed class GpsExceptionService(IGpsExceptionRepository repository) : IGpsExceptionService
{
    public Task<LifecycleResult> Submit(Guid driverId, Guid deliveryId, GpsExceptionRequest request, CancellationToken token) => repository.Submit(driverId, deliveryId, request, token);
    public Task<IReadOnlyList<GpsExceptionItem>> List(CancellationToken token) => repository.List(token);
    public Task<LifecycleResult> Review(Guid id, GpsExceptionReview request, string actor, CancellationToken token) => repository.Review(id, request, actor, token);
    public Task<byte[]?> Photo(Guid id, CancellationToken token) => repository.Photo(id, token);
}
