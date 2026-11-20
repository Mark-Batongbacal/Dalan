using System.Security.Claims;
using Dalan.Application.Deliveries;
using Dalan.Application.Dispatch;
using Dalan.Application.Drivers;
using Dalan.Application.Vehicles;
using Dalan.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dalan.Web.Controllers;

[Authorize(Roles="Dispatcher")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class DeliveryWorkflowController(IWarehousePreferenceService warehouses, IDriverService drivers,
    IVehicleService vehicles, IDeliveryService deliveries) : Controller
{
    public async Task<IActionResult> Index(CancellationToken token)
    {
        var id=User.FindFirstValue(ClaimTypes.NameIdentifier);
        if(id is null) return Forbid();
        var work=await deliveries.ListForPlanningAsync(token);
        return View(new DeliveryWorkflowViewModel(await warehouses.GetAsync(id,token),(await drivers.ListAsync(token)).Count,
            (await vehicles.ListAsync(token)).Count,work.Count(x=>x.Status is Dalan.Domain.Deliveries.DeliveryStatus.Pending or Dalan.Domain.Deliveries.DeliveryStatus.Assigned),
            work.Count(x=>x.Status == Dalan.Domain.Deliveries.DeliveryStatus.Assigned)));
    }
    public async Task<IActionResult> Assignments(CancellationToken token) => View(new DeliveryAssignmentsViewModel(
        (await deliveries.ListForPlanningAsync(token)).Where(x=>x.Status is Dalan.Domain.Deliveries.DeliveryStatus.Pending or Dalan.Domain.Deliveries.DeliveryStatus.Assigned).ToArray(),
        await drivers.ListAsync(token),await vehicles.ListAsync(token)));
}
