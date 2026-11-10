using Dalan.Domain.Dispatch;

namespace Dalan.Application.Dispatch;

public interface IDispatchPlanReader
{
    Task<DispatchPlan?> GetByIdAsync(
        Guid id, CancellationToken cancellationToken = default);
}
