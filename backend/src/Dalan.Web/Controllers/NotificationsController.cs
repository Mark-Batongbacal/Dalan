using Dalan.Application.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dalan.Web.Controllers;

public sealed record NotificationsViewModel(bool Enabled, IReadOnlyList<EmailDeliveryStatus> Deliveries);

[Authorize(Roles = "Admin,Dispatcher")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NotificationsController(IOperationsNotificationService notifications, ResendOptions options) : Controller
{
    [HttpGet] public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(new NotificationsViewModel(options.Enabled, options.Enabled ? await notifications.RecentAsync(cancellationToken) : []));
}
