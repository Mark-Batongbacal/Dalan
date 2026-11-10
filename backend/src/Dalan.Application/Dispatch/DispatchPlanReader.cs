using Dalan.Domain.Dispatch;

namespace Dalan.Application.Dispatch;

public sealed class DispatchPlanReader(IDispatchRepository repository)
    : IDispatchPlanReader
{
    public Task<DispatchPlan?> GetByIdAsync(
        Guid id, CancellationToken cancellationToken = default)
        => repository.GetByIdAsync(id, cancellationToken);
}
