using System.Security.Claims;
using Dalan.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dalan.Infrastructure.Auditing;

/// <summary>
/// Audits account lifecycle actions by MVC controller/action name. This also
/// handles the separately developed DLN-30/31/32 controllers after integration,
/// without accessing or serializing passwords, credential tokens, or request bodies.
/// Enable only after the AddAccountAccessAudit migration is explicitly applied.
/// </summary>
public sealed class AccountAccessAuditFilter(
    DalanDbContext db, TimeProvider clock, IConfiguration configuration,
    ILogger<AccountAccessAuditFilter> logger) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var descriptor = context.ActionDescriptor as ControllerActionDescriptor;
        var action = descriptor is null ? null : Classify(
            descriptor.ControllerName, descriptor.ActionName, context.HttpContext.Request.Method);
        var executed = await next();
        if (action is null || !bool.TryParse(configuration["AccountAudit:Enabled"], out var enabled) || !enabled)
            return;

        var result = executed.Result;
        var successful = result is RedirectToActionResult or RedirectResult or LocalRedirectResult
            or SignInResult or OkResult or OkObjectResult or AcceptedResult or AcceptedAtActionResult
            or AcceptedObjectResult or NoContentResult;
        var outcome = executed.Exception is not null && !executed.ExceptionHandled
            ? "Error"
            : successful ? action == "DriverRecoveryRequested" ? "Requested" : "Succeeded" : "Rejected";

        // For "Create" actions, rendering the form with validation errors is not a success.
        // The request/credential payload is never logged or serialized.
        var values = context.ActionArguments.Values.Where(value => value is not null).ToArray();
        var driverId = ReadDriverId(context, values);
        var email = ReadString(values, "Email");
        var reason = action is "DriverMobileDisabled" or "DriverMobileEnabled"
            ? SafeReason(ReadString(values, "Reason")) : null;

        string? targetUserId = null;
        try
        {
            if (driverId is Guid id)
                targetUserId = await db.DriverAccounts.AsNoTracking()
                    .Where(x => x.DriverId == id).Select(x => x.UserId)
                    .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

            if (targetUserId is null && !string.IsNullOrWhiteSpace(email))
            {
                var normalized = email.Trim().ToUpperInvariant();
                targetUserId = await db.Users.AsNoTracking()
                    .Where(x => x.NormalizedEmail == normalized)
                    .Select(x => x.Id).FirstOrDefaultAsync(context.HttpContext.RequestAborted);
            }

            if (driverId is null && targetUserId is not null)
                driverId = await db.DriverAccounts.AsNoTracking()
                    .Where(x => x.UserId == targetUserId).Select(x => (Guid?)x.DriverId)
                    .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

            // Avoid unauthenticated unknown-address login spam in the SQL table;
            // failed requests for known accounts and authenticated mutations remain auditable.
            if (targetUserId is null && (action is "DriverLogin" or "StaffLogin" or "DriverRecoveryRequested")
                && outcome is "Rejected" or "Requested")
                return;

            db.AccountAccessAudits.Add(new AccountAccessAudit
            {
                OccurredAtUtc = clock.GetUtcNow(),
                Action = action,
                Outcome = outcome,
                ActorUserId = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier),
                TargetUserId = targetUserId,
                DriverId = driverId,
                Reason = reason,
                CorrelationId = Clip(context.HttpContext.TraceIdentifier, 128)
            });
            await db.SaveChangesAsync(context.HttpContext.RequestAborted);
        }
        catch (Exception error) when (!context.HttpContext.RequestAborted.IsCancellationRequested)
        {
            // Do not log models or exception messages, which might contain PII.
            // The audit event is best-effort after the original action, not an
            // atomic replacement for DLN-30/31/32 controller transaction logging.
            logger.LogError("Could not persist {AuditAction} audit event. ErrorType={ErrorType}",
                action, error.GetType().Name);
        }
    }

    private static string? Classify(string controller, string action, string method)
    {
        if (!string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
            return null;
        return (controller, action) switch
        {
            ("DriverAccounts", "Create") => "DriverAccountCreated",
            ("StaffAccounts", "Create") => "StaffAccountCreated",
            ("Account", "Login") => "StaffLogin",
            ("DriverTasks", "Login") => "DriverLogin",
            ("DriverInvitations", "Issue") => "DriverInvitationIssued",
            ("DriverInvitations", "Activate") => "DriverInvitationActivated",
            ("DriverRecovery", "RequestReset") => "DriverRecoveryRequested",
            ("DriverRecovery", "Reset") => "DriverPasswordReset",
            ("DriverAccess", "Disable") => "DriverMobileDisabled",
            ("DriverAccess", "Enable") => "DriverMobileEnabled",
            _ => null
        };
    }

    private static Guid? ReadDriverId(ActionExecutingContext context, object?[] args)
    {
        if (context.RouteData.Values.TryGetValue("id", out var raw)
            && Guid.TryParse(raw?.ToString(), out var id) && id != Guid.Empty)
            return id;
        foreach (var value in args)
        {
            if (value is null) continue;
            var property = value.GetType().GetProperty("DriverId");
            if (property?.PropertyType == typeof(Guid)
                && property.GetValue(value) is Guid driverId && driverId != Guid.Empty)
                return driverId;
        }
        return null;
    }

    private static string? ReadString(object?[] args, string property)
    {
        foreach (var value in args)
        {
            if (value is null) continue;
            var found = value.GetType().GetProperty(property);
            if (found?.PropertyType == typeof(string))
                return found.GetValue(value) as string;
        }
        return null;
    }

    private static string? SafeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return null;
        var value = string.Join(" ", reason.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (value.Contains("://", StringComparison.Ordinal)
            || value.Contains("token", StringComparison.OrdinalIgnoreCase)
            || value.Contains("password", StringComparison.OrdinalIgnoreCase)
            || value.Contains('@'))
            return "[redacted]";
        return Clip(value, 240);
    }

    private static string? Clip(string? value, int max)
        => value is null ? null : value.Length <= max ? value : value[..max];
}
