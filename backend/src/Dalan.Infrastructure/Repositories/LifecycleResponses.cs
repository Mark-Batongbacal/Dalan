using Dalan.Application.Identity;
namespace Dalan.Infrastructure.Repositories;
public abstract class LifecycleResponses
{
    protected static LifecycleResult StatusCode(int status, object? value = null) => new(status, value);
    protected static LifecycleResult Ok(object value) => new(200, value);
    protected static LifecycleResult Accepted(object value) => new(202, value);
    protected static LifecycleResult BadRequest(object value) => new(400, value);
    protected static LifecycleResult NotFound() => new(404);
    protected static LifecycleResult Conflict(object value) => new(409, value);
}
