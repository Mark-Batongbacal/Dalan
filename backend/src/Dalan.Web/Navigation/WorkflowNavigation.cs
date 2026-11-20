using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Dalan.Web.Navigation;

public sealed class WorkflowNavigation : IResultFilter
{
    private static readonly HashSet<string> Controllers = new(StringComparer.OrdinalIgnoreCase) {
        "Warehouse", "Vehicles", "Drivers", "Deliveries", "DeliveryBatch", "DeliveryWorkflow", "DispatchBoard", "FleetPlanner", "FleetTracking"
    };

    public static bool IsActive(HttpContext context) => context.User.IsInRole("Dispatcher")
        && context.Request.Query["workflow"] == "guide"
        && Controllers.Contains(context.GetRouteValue("controller")?.ToString() ?? "");

    public void OnResultExecuting(ResultExecutingContext context) {
        if (!IsActive(context.HttpContext) || context.Result is not RedirectToActionResult redirect) return;
        var controller = redirect.ControllerName ?? context.RouteData.Values["controller"]?.ToString();
        if (controller is null || !Controllers.Contains(controller)) return;
        redirect.RouteValues ??= new();
        redirect.RouteValues["workflow"] = "guide";
    }
    public void OnResultExecuted(ResultExecutedContext context) { }
}
