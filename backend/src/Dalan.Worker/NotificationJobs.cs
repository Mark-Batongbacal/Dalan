using Dalan.Application.Notifications;
using Dalan.Application.Tracking;

namespace Dalan.Worker;

public sealed class NotificationJobs(IOperationsNotificationService notifications,
    IFleetTrackingService tracking, Dalan.Infrastructure.Notifications.IDriverCredentialEmailOutbox credentials)
{
    public async Task RetryAsync(CancellationToken cancellationToken)
    {
        await notifications.RetryPendingAsync(cancellationToken);
        await credentials.RetryPendingAsync(cancellationToken);
    }
    public Task CleanupTrackingAsync(CancellationToken cancellationToken) => tracking.CleanupAsync(cancellationToken);
}
