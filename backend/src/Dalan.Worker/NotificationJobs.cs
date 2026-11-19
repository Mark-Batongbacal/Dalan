using Dalan.Application.Notifications;
using Dalan.Application.Tracking;

namespace Dalan.Worker;

public sealed class NotificationJobs(IOperationsNotificationService notifications, IFleetTrackingService tracking)
{
    public Task RetryAsync(CancellationToken cancellationToken) => notifications.RetryPendingAsync(cancellationToken);
    public Task CleanupTrackingAsync(CancellationToken cancellationToken) => tracking.CleanupAsync(cancellationToken);
}
