using System.Security.Claims;
using Dalan.Application.Deliveries;
using Dalan.Application.Dispatch;
using Dalan.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dalan.Web.Controllers;

[Authorize(Roles="Dispatcher")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class DeliveryBatchController(IWarehousePreferenceService warehouses, IDeliveryBatchService batches, TimeProvider clock) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken token)
    {
        var id=User.FindFirstValue(ClaimTypes.NameIdentifier);
        if(id is null) return Forbid();
        var now=clock.GetUtcNow();
        return View(new DeliveryBatchViewModel { Warehouse=await warehouses.GetAsync(id,token),
            ExpectedDeliveryAtUtc=new DateTimeOffset(now.Year,now.Month,now.Day,now.Hour,now.Minute/30*30,0,TimeSpan.Zero).AddMinutes(30) });
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(DeliveryBatchViewModel model, CancellationToken token)
    {
        var id=User.FindFirstValue(ClaimTypes.NameIdentifier);
        if(id is null) return Forbid();
        model.Warehouse=await warehouses.GetAsync(id,token);
        model.Rows ??= [];
        if(model.Warehouse is null) ModelState.AddModelError("","Save your default warehouse in step 1 first.");
        if(model.Rows.Count is <1 or >15) ModelState.AddModelError("","Add 1 to 15 deliveries.");
        for(var i=0;i<model.Rows.Count;i++)
        {
            if(model.Rows[i].DropoffLatitude is null || model.Rows[i].DropoffLongitude is null)
            {
                ModelState.Remove($"Rows[{i}].DropoffLatitude");
                ModelState.Remove($"Rows[{i}].DropoffLongitude");
                ModelState.AddModelError("",$"Confirm the destination pin for delivery {i+1} on the map.");
            }
        }
        if(model.Rows.Count==0) model.Rows.Add(new());
        if(!ModelState.IsValid) return View(model);
        var w=model.Warehouse!;
        var result=await batches.CreateAsync(model.Rows.Select(row=>new CreateDeliveryCommand(row.RecipientName,w.Name,w.Latitude,w.Longitude,
            row.DropoffAddress,row.DropoffLatitude!.Value,row.DropoffLongitude!.Value,row.PayloadWeightKg,model.ExpectedDeliveryAtUtc)).ToArray(),token);
        if(!result.Created) { ModelState.AddModelError("",result.Error!); return View(model); }
        TempData["WorkflowNotice"]=$"{result.DeliveryIds.Count} deliveries created. Assign their drivers and vehicles next.";
        return RedirectToAction("Assignments","DeliveryWorkflow");
    }
}
